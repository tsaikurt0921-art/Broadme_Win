using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Broadme.Win.Services.Auth;

namespace Broadme.Win.Services.Networking;

public sealed class MjpegServer
{
    private readonly HttpListener _listener = new();
    private readonly object _lock = new();
    private readonly List<HttpListenerResponse> _streamClients = new();
    private readonly List<HttpListenerResponse> _controlStreamClients = new();

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
        if (_listener.IsListening) return;

        var normalized = string.IsNullOrWhiteSpace(bindIp) ? "0.0.0.0" : bindIp.Trim();
        if (normalized == "0.0.0.0" || normalized == "*" || normalized == "+")
        {
            _listener.Prefixes.Add($"http://+:{port}/");
        }
        else
        {
            _listener.Prefixes.Add($"http://{normalized}:{port}/");
        }

        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            foreach (var client in _streamClients.Concat(_controlStreamClients))
            {
                try { client.OutputStream.Close(); } catch { }
            }
            _streamClients.Clear();
            _controlStreamClients.Clear();
            ClientCountChanged?.Invoke(0);
        }
        if (_listener.IsListening) _listener.Stop();
    }

    public async Task PushFrameAsync(byte[] jpeg)
    {
        await PushToClientsAsync(jpeg, _streamClients);
        await PushToClientsAsync(jpeg, _controlStreamClients);
    }

    private async Task PushToClientsAsync(byte[] jpeg, List<HttpListenerResponse> target)
    {
        List<HttpListenerResponse> snapshot;
        lock (_lock) snapshot = target.ToList();

        var header = Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
        var tail = Encoding.ASCII.GetBytes("\r\n");

        foreach (var res in snapshot)
        {
            try
            {
                await res.OutputStream.WriteAsync(header);
                await res.OutputStream.WriteAsync(jpeg);
                await res.OutputStream.WriteAsync(tail);
                await res.OutputStream.FlushAsync();
            }
            catch
            {
                lock (_lock)
                {
                    target.Remove(res);
                    ClientCountChanged?.Invoke(_streamClients.Count);
                }
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";

        if (ctx.Request.HttpMethod == "GET")
        {
            switch (path)
            {
                case "/":
                    ctx.Response.StatusCode = 302;
                    ctx.Response.RedirectLocation = "/stream";
                    ctx.Response.Close();
                    return;
                case "/stream":
                    AddStreamClient(ctx.Response, _streamClients);
                    return;
                case "/stream-control":
                    AddStreamClient(ctx.Response, _controlStreamClients);
                    return;
                case "/control":
                    await ServeControlPage(ctx.Response);
                    return;
                case "/apple-touch-icon.png":
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
            }
        }

        if (ctx.Request.HttpMethod == "POST")
        {
            switch (path)
            {
                case "/api/auth/pin":
                    await HandlePinAuth(ctx);
                    return;
                case "/api/input":
                    await HandleInput(ctx);
                    return;
                case "/api/control/check":
                    await HandleControlCheck(ctx);
                    return;
                case "/api/control/revoke":
                    await HandleControlRevoke(ctx);
                    return;
                case "/api/upload-photo":
                    await HandleUploadPhoto(ctx);
                    return;
                default:
                    await WriteJson(ctx.Response, 404, new { success = false, error = "Not Found" });
                    return;
            }
        }

        ctx.Response.StatusCode = 405;
        ctx.Response.Close();
    }

    private void AddStreamClient(HttpListenerResponse response, List<HttpListenerResponse> list)
    {
        response.StatusCode = 200;
        response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        response.SendChunked = true;
        lock (_lock)
        {
            list.Add(response);
            ClientCountChanged?.Invoke(_streamClients.Count);
        }
    }

    private async Task ServeControlPage(HttpListenerResponse response)
    {
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "control.html");
        var html = File.Exists(htmlPath) ? await File.ReadAllTextAsync(htmlPath) : "<h1>Broadme control page missing</h1>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private async Task HandlePinAuth(HttpListenerContext ctx)
    {
        var req = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.InputStream);
        var pin = req != null && req.TryGetValue("pin", out var p) ? p : string.Empty;

        if (!_pinManager.Validate(pin ?? string.Empty))
        {
            await WriteJson(ctx.Response, 401, new { success = false, error = "PIN 碼錯誤或已過期" });
            return;
        }

        var token = _sessionManager.CreateSession();
        await WriteJson(ctx.Response, 200, new { success = true, token, message = "驗證成功", expires_in = 600 });
    }

    private async Task HandleControlCheck(HttpListenerContext ctx)
    {
        var req = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.InputStream);
        var pin = req != null && req.TryGetValue("controlPin", out var p) ? p : string.Empty;

        if (_authManager.IsAuthorized(pin ?? string.Empty))
        {
            var token = _authManager.GetToken(pin ?? string.Empty);
            if (string.IsNullOrWhiteSpace(token) || !_sessionManager.ValidateAndExtend(token))
            {
                token = _sessionManager.CreateSession();
                _authManager.BindToken(pin ?? string.Empty, token);
            }

            await WriteJson(ctx.Response, 200, new { success = true, authorized = true, anyoneAuthorized = true, token });
            return;
        }

        var anyone = _authManager.HasAnyAuthorization();
        await WriteJson(ctx.Response, 200, new { success = true, authorized = false, anyoneAuthorized = anyone });
    }

    private async Task HandleControlRevoke(HttpListenerContext ctx)
    {
        var req = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.InputStream);
        var token = req != null && req.TryGetValue("token", out var t) ? t : string.Empty;
        _sessionManager.EndSession(token ?? string.Empty);
        _authManager.RevokeByToken(token ?? string.Empty);
        await WriteJson(ctx.Response, 200, new { success = true, message = "已撤銷授權" });
    }

    private async Task HandleInput(HttpListenerContext ctx)
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.InputStream);
        var root = doc.RootElement;

        var token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
        if (!_sessionManager.ValidateAndExtend(token ?? string.Empty))
        {
            await WriteJson(ctx.Response, 401, new { success = false, error = "Token 無效", require_reauth = true });
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
        await WriteJson(ctx.Response, 200, new { success = true });
    }

    private async Task HandleUploadPhoto(HttpListenerContext ctx)
    {
        using var ms = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(ms);
        var data = ms.ToArray();
        PhotoUploaded?.Invoke(data);
        await WriteJson(ctx.Response, 200, new { success = true });
    }

    private static async Task WriteJson(HttpListenerResponse response, int statusCode, object payload)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

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
