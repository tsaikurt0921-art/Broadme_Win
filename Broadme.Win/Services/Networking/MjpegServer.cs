using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Broadme.Win.Services.Auth;

namespace Broadme.Win.Services.Networking;

public sealed class MjpegServer
{
    private TcpListener? _listener;
    private readonly object _lock = new();
    private readonly List<StreamClient> _clients = new();
    
    private readonly ControlAuthManager _authManager;
    private readonly ControlPinManager _pinManager;
    private readonly SessionManager _sessionManager;
    private CancellationTokenSource? _cts;

    public event Action<int>? ClientCountChanged;
    public event Action<byte[]>? PhotoUploaded;
    public event Action<ControlCommand>? ControlCommandReceived;

    public MjpegServer(ControlAuthManager authManager, ControlPinManager pinManager, SessionManager sessionManager)
    {
        _authManager = authManager;
        _pinManager = pinManager;
        _sessionManager = sessionManager;
    }

    public void Start(string bindIp, int port)
    {
        if (_listener != null) return;

        try
        {
            var ip = string.IsNullOrWhiteSpace(bindIp) || bindIp == "0.0.0.0" 
                ? IPAddress.Any 
                : IPAddress.Parse(bindIp);

            _listener = new TcpListener(ip, port);
            _listener.Start();
            
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            throw new Exception($"無法啟動網路伺服器: {ex.Message}。請確認通訊埠 {port} 未被佔用。", ex);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;

        lock (_lock)
        {
            foreach (var client in _clients)
            {
                try { client.Stream.Close(); } catch { }
                try { client.Tcp.Close(); } catch { }
            }
            _clients.Clear();
            ClientCountChanged?.Invoke(0);
        }
    }

    public async Task PushFrameAsync(byte[] jpeg)
    {
        List<StreamClient> snapshot;
        lock (_lock) snapshot = _clients.ToList();

        if (snapshot.Count == 0) return;

        var header = Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
        var tail = Encoding.ASCII.GetBytes("\r\n");

        var tasks = snapshot.Select(async client =>
        {
            try
            {
                await client.Stream.WriteAsync(header);
                await client.Stream.WriteAsync(jpeg);
                await client.Stream.WriteAsync(tail);
                await client.Stream.FlushAsync();
            }
            catch
            {
                lock (_lock)
                {
                    if (_clients.Remove(client))
                    {
                        ClientCountChanged?.Invoke(_clients.Count);
                    }
                }
                try { client.Tcp.Close(); } catch { }
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            using (tcp)
            using (var stream = tcp.GetStream())
            {
                // 讀取請求行 (e.g. GET /control HTTP/1.1)
                var requestLine = await ReadLineAsync(stream, ct);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0].ToUpperInvariant();
                var url = parts[1];
                var path = url.Split('?')[0];

                // 讀取標頭
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var headerLine = await ReadLineAsync(stream, ct);
                    if (string.IsNullOrWhiteSpace(headerLine)) break;
                    var hParts = headerLine.Split(':', 2);
                    if (hParts.Length == 2) headers[hParts[0].Trim()] = hParts[1].Trim();
                }

                if (method == "GET")
                {
                    switch (path)
                    {
                        case "/":
                        case "/index.html":
                            await SendRedirect(stream, "/control");
                            break;
                        case "/stream":
                        case "/stream-control":
                            await HandleMjpegStream(tcp, stream);
                            break;
                        case "/control":
                            await ServeStaticFile(stream, "control.html", "text/html");
                            break;
                        default:
                            await SendStatus(stream, 404, "Not Found");
                            break;
                    }
                }
                else if (method == "POST")
                {
                    // 讀取 Body
                    long contentLength = 0;
                    if (headers.TryGetValue("Content-Length", out var clStr)) long.TryParse(clStr, out contentLength);

                    byte[] body = Array.Empty<byte>();
                    if (contentLength > 0)
                    {
                        body = new byte[contentLength];
                        int totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            int r = await stream.ReadAsync(body, totalRead, (int)(contentLength - totalRead), ct);
                            if (r <= 0) break;
                            totalRead += r;
                        }
                    }

                    switch (path)
                    {
                        case "/api/auth/pin":
                            await HandlePinAuth(stream, body);
                            break;
                        case "/api/input":
                            await HandleInput(stream, body);
                            break;
                        case "/api/control/check":
                            await HandleControlCheck(stream, body);
                            break;
                        case "/api/control/revoke":
                            await HandleControlRevoke(stream, body);
                            break;
                        case "/api/upload-photo":
                            await HandleUploadPhoto(stream, body);
                            break;
                        default:
                            await SendJson(stream, 404, new { success = false, error = "Not Found" });
                            break;
                    }
                }
                else
                {
                    await SendStatus(stream, 405, "Method Not Allowed");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Client handling error: {ex.Message}");
        }
    }

    private async Task HandleMjpegStream(TcpClient tcp, NetworkStream stream)
    {
        var header = "HTTP/1.1 200 OK\r\n" +
                     "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n" +
                     "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                     "Connection: keep-alive\r\n\r\n";
        
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.FlushAsync();

        var client = new StreamClient(tcp, stream);
        lock (_lock)
        {
            _clients.Add(client);
            ClientCountChanged?.Invoke(_clients.Count);
        }

        // 保持連線開啟，直到客戶端斷開或伺服器停止
        var tcs = new TaskCompletionSource();
        using (_cts?.Token.Register(() => tcs.TrySetResult()))
        {
            await tcs.Task;
        }
    }

    private async Task HandlePinAuth(NetworkStream stream, byte[] body)
    {
        var req = body.Length > 0 ? JsonSerializer.Deserialize<Dictionary<string, string>>(body) : null;
        var pin = req != null && req.TryGetValue("pin", out var p) ? p : string.Empty;

        if (!_pinManager.Validate(pin ?? string.Empty))
        {
            await SendJson(stream, 401, new { success = false, error = "PIN 碼錯誤或已過期" });
            return;
        }

        var token = _sessionManager.CreateSession();
        await SendJson(stream, 200, new { success = true, token, message = "驗證成功", expires_in = 600 });
    }

    private async Task HandleControlCheck(NetworkStream stream, byte[] body)
    {
        var req = body.Length > 0 ? JsonSerializer.Deserialize<Dictionary<string, string>>(body) : null;
        var pin = req != null && req.TryGetValue("controlPin", out var p) ? p : string.Empty;

        if (_authManager.IsAuthorized(pin ?? string.Empty))
        {
            var token = _authManager.GetToken(pin ?? string.Empty);
            if (string.IsNullOrWhiteSpace(token) || !_sessionManager.ValidateAndExtend(token))
            {
                token = _sessionManager.CreateSession();
                _authManager.BindToken(pin ?? string.Empty, token);
            }

            await SendJson(stream, 200, new { success = true, authorized = true, anyoneAuthorized = true, token });
            return;
        }

        var anyone = _authManager.HasAnyAuthorization();
        await SendJson(stream, 200, new { success = true, authorized = false, anyoneAuthorized = anyone });
    }

    private async Task HandleControlRevoke(NetworkStream stream, byte[] body)
    {
        var req = body.Length > 0 ? JsonSerializer.Deserialize<Dictionary<string, string>>(body) : null;
        var token = req != null && req.TryGetValue("token", out var t) ? t : string.Empty;
        _sessionManager.EndSession(token ?? string.Empty);
        _authManager.RevokeByToken(token ?? string.Empty);
        await SendJson(stream, 200, new { success = true, message = "已撤銷授權" });
    }

    private async Task HandleInput(NetworkStream stream, byte[] body)
    {
        if (body.Length == 0)
        {
            await SendJson(stream, 400, new { success = false, error = "Bad Request" });
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
        if (!_sessionManager.ValidateAndExtend(token ?? string.Empty))
        {
            await SendJson(stream, 401, new { success = false, error = "Token 無效", require_reauth = true });
            return;
        }

        var cmd = new ControlCommand
        {
            Type = root.TryGetProperty("type", out var tp) ? tp.GetString() ?? string.Empty : string.Empty,
            X = root.TryGetProperty("x", out var x) && x.TryGetDouble(out var xv) ? xv : 0.5,
            Y = root.TryGetProperty("y", out var y) && y.TryGetDouble(out var yv) ? yv : 0.5,
            DeltaX = root.TryGetProperty("deltaX", out var dx) && dx.TryGetInt32(out var dxi) ? dxi : 0,
            DeltaY = root.TryGetProperty("deltaY", out var dy) && dy.TryGetInt32(out var dyi) ? dyi : 0,
            Delta = root.TryGetProperty("delta", out var d) && d.TryGetInt32(out var di) ? di : 0,
            Color = root.TryGetProperty("color", out var c) ? c.GetString() ?? "#ef4444" : "#ef4444",
            Width = root.TryGetProperty("width", out var w) && w.TryGetDouble(out var wd) ? wd : 4
        };

        ControlCommandReceived?.Invoke(cmd);
        await SendJson(stream, 200, new { success = true });
    }

    private async Task HandleUploadPhoto(NetworkStream stream, byte[] body)
    {
        PhotoUploaded?.Invoke(body);
        await SendJson(stream, 200, new { success = true });
    }

    private async Task ServeStaticFile(NetworkStream stream, string fileName, string contentType)
    {
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
        byte[] content;
        if (File.Exists(htmlPath))
        {
            content = await File.ReadAllBytesAsync(htmlPath);
        }
        else
        {
            content = Encoding.UTF8.GetBytes($"<h1>Broadme file {fileName} missing</h1>");
        }

        var header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(content);
        await stream.FlushAsync();
    }

    private async Task SendRedirect(NetworkStream stream, string location)
    {
        var header = $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.FlushAsync();
    }

    private async Task SendStatus(NetworkStream stream, int code, string message)
    {
        var header = $"HTTP/1.1 {code} {message}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.FlushAsync();
    }

    private async Task SendJson(NetworkStream stream, int code, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = Encoding.UTF8.GetBytes(json);
        var header = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(content);
        await stream.FlushAsync();
    }

    private async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, 0, 1, ct);
            if (read <= 0) break;
            char c = (char)buffer[0];
            if (c == '\n') break;
            if (c != '\r') line.Append(c);
        }
        return line.ToString();
    }
}

internal record StreamClient(TcpClient Tcp, NetworkStream Stream);

public sealed class ControlCommand
{
    public string Type { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
    public int Delta { get; set; }
    public string Color { get; set; } = "#ef4444";
    public double Width { get; set; } = 4;
}
