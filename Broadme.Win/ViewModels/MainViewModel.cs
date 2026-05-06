using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Broadme.Win.Models;
using Broadme.Win.Services.Annotation;
using Broadme.Win.Services.Auth;
using Broadme.Win.Services.Capture;
using Broadme.Win.Services.Input;
using Broadme.Win.Services.Logging;
using Broadme.Win.Services.Networking;
using Broadme.Win.Views;

namespace Broadme.Win.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly MjpegServer _server;
    private readonly IScreenCaptureService _capture;
    private readonly BroadmeLogger _logger;
    private readonly InputSimulator _input = new();
    private readonly AnnotationOverlayWindow _annotation = AnnotationOverlayWindow.Instance;
    private readonly AppConfig _config = AppConfig.Load();
    private readonly CancellationTokenSource _cts = new();

    private readonly ControlAuthManager _controlAuth = new();
    private readonly ControlPinManager _controlPin = new();
    private readonly SessionManager _sessions = new();

    private bool _isStreaming;
    private int _clientCount;
    private string _statusMessage = "待命中";
    private bool _networkHintShown;

    public MainViewModel()
    {
        _server = new MjpegServer(_controlAuth, _controlPin, _sessions);
        _capture = new ScreenCaptureService();
        _logger = new BroadmeLogger();

        ResolutionOptions = new List<string> { "1600x900", "1280x720", "1280x800", "1024x768" };
        FpsOptions = new List<int> { 24, 20, 15, 10, 8 };
        NetworkInterfaces = GetAvailableInterfaces();

        SelectedResolution = _config.SelectedResolution;
        SelectedFps = _config.SelectedFps;
        SelectedInterface = NetworkInterfaces.FirstOrDefault();

        _server.ClientCountChanged += c => ClientCount = c;
        _server.ControlCommandReceived += HandleControlCommand;
        _server.PhotoUploaded += data => _ = HandlePhotoUploadedAsync(data);

        ToggleBroadcastCommand = new RelayCommand(async () => await ToggleBroadcastAsync());
        CopyUrlCommand = new RelayCommand(CopyUrl);
        OpenControlAuthCommand = new RelayCommand(OpenControlAuth);
        OpenLogCommand = new RelayCommand(OpenLog);
        ShowQrCommand = new RelayCommand(ShowQrWindow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<string> ResolutionOptions { get; }
    public List<int> FpsOptions { get; }
    public List<NetworkInterfaceOption> NetworkInterfaces { get; }

    private string _selectedResolution = "1280x720";
    public string SelectedResolution
    {
        get => _selectedResolution;
        set { _selectedResolution = value; OnPropertyChanged(); }
    }

    private int _selectedFps = 15;
    public int SelectedFps
    {
        get => _selectedFps;
        set { _selectedFps = value; OnPropertyChanged(); }
    }

    private NetworkInterfaceOption? _selectedInterface;
    public NetworkInterfaceOption? SelectedInterface
    {
        get => _selectedInterface;
        set
        {
            _selectedInterface = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentIp));
            OnPropertyChanged(nameof(StreamUrl));
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            _isStreaming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartStopText));
            OnPropertyChanged(nameof(IsNotStreaming));
        }
    }

    public bool IsNotStreaming => !IsStreaming;

    public int ClientCount
    {
        get => _clientCount;
        set 
        { 
            if (_clientCount != value)
            {
                _clientCount = value; 
                OnPropertyChanged();
                _ = _logger.LogAsync($"連線人數已變更: {value}");
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string CurrentIp
    {
        get
        {
            var ip = SelectedInterface?.IpAddress;
            if (string.IsNullOrWhiteSpace(ip)) ip = GetFallbackLocalIp() ?? "localhost";
            return ip;
        }
    }

    public string StreamUrl => $"http://{CurrentIp}:{_config.StreamPort}/stream";

    public string StartStopText => IsStreaming ? "結束" : "開始";

    public ICommand ToggleBroadcastCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand OpenControlAuthCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand ShowQrCommand { get; }

    private async Task ToggleBroadcastAsync()
    {
        if (IsStreaming)
        {
            await _capture.StopAsync();
            _server.Stop();
            _controlAuth.RevokeAll();
            _controlPin.Revoke();
            _annotation.ClearAll();
            IsStreaming = false;
            StatusMessage = "已停止";
            await _logger.LogAsync("廣播已停止");
            return;
        }

        _config.SelectedResolution = SelectedResolution;
        _config.SelectedFps = SelectedFps;
        _config.BindIp = SelectedInterface?.IpAddress ?? "0.0.0.0";
        _config.Save();

        var pin = _controlPin.Generate();
        await _logger.LogAsync($"控制 PIN 已生成: {pin}");

        try
        {
            _server.Start(_config.BindIp, _config.StreamPort);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"啟動廣播失敗: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"無法啟動網路廣播！\n\n原因: {ex.Message}\n\n請確認通訊埠 {_config.StreamPort} 沒有被其他程式佔用。",
                "廣播啟動失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            IsStreaming = false;
            return;
        }

        try
        {
            await _capture.StartAsync(
                _config.SelectedFps,
                _config.SelectedResolution,
                async (bytes, _) => await _server.PushFrameAsync(bytes),
                _cts.Token);
        }
        catch (Exception ex)
        {
            _server.Stop();
            await _logger.LogAsync($"啟動螢幕擷取失敗: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"螢幕擷取啟動失敗！\n\n原因: {ex.Message}",
                "擷取啟動失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            IsStreaming = false;
            return;
        }

        IsStreaming = true;
        StatusMessage = $"廣播中: {StreamUrl} | 控制PIN: {pin}";
        await _logger.LogAsync(StatusMessage);

        if (!_networkHintShown)
        {
            _networkHintShown = true;
            System.Windows.MessageBox.Show(
                NetworkReadinessService.BuildFirstRunHint(_config.StreamPort),
                "Broadme 網路提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void CopyUrl()
    {
        System.Windows.Clipboard.SetText(StreamUrl);
        StatusMessage = "已複製 URL";
    }

    private void ShowQrWindow()
    {
        var w = new QrCodeWindow(StreamUrl);
        w.ShowDialog();
    }

    private void OpenControlAuth()
    {
        var prompt = new ControlAuthorizationWindow(pin =>
        {
            _controlAuth.Authorize(pin);
            StatusMessage = $"已授權控制 PIN: {pin}";
        }, _config.StreamPort, CurrentIp);

        prompt.ShowDialog();
    }

    private void OpenLog()
    {
        var path = _logger.GetLogPath();
        if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task HandlePhotoUploadedAsync(byte[] data)
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"Broadme_Photo_{Guid.NewGuid():N}.jpg");
            await File.WriteAllBytesAsync(file, data);
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            await _logger.LogAsync($"收到照片上傳: {data.Length} bytes");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"照片處理失敗: {ex.Message}");
        }
    }

    private void HandleControlCommand(ControlCommand cmd)
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        var x = bounds.Left + (cmd.X * bounds.Width);
        var y = bounds.Top + (cmd.Y * bounds.Height);
        var point = new System.Windows.Point(x - bounds.Left, y - bounds.Top);

        switch (cmd.Type)
        {
            case "click":
                _input.LeftClick(x, y);
                break;
            case "doubleClick":
                _input.DoubleClick(x, y);
                break;
            case "move":
                _input.MoveMouse(x, y);
                break;
            case "scroll":
                var dy = cmd.DeltaY != 0 ? cmd.DeltaY : cmd.Delta;
                _input.Scroll(cmd.DeltaX, dy);
                break;
            case "annotationStart":
                _annotation.StartStroke(point, cmd.Color, cmd.Width);
                break;
            case "annotationMove":
                _annotation.MoveStroke(point);
                break;
            case "annotationEnd":
                _annotation.EndStroke(point);
                break;
            case "annotationClear":
                _annotation.ClearAll();
                break;
            case "annotationUndo":
                _annotation.UndoStroke();
                break;
            case "annotationRedo":
                _annotation.RedoStroke();
                break;
            case "clearAnnotations":
                _annotation.ClearAll();
                break;
        }
    }

    private static List<NetworkInterfaceOption> GetAvailableInterfaces()
    {
        var result = new List<NetworkInterfaceOption>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ip = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

            if (string.IsNullOrWhiteSpace(ip)) continue;
            if (ip.StartsWith("169.254.")) continue;

            result.Add(new NetworkInterfaceOption
            {
                Name = ni.Name,
                DisplayName = ni.Description,
                IpAddress = ip
            });
        }

        return result
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetFallbackLocalIp()
    {
        return GetAvailableInterfaces().FirstOrDefault()?.IpAddress;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class NetworkInterfaceOption
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;

    public string Label => $"{DisplayName} ({IpAddress})";
}
