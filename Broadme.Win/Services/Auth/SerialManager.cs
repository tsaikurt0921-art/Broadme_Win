using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broadme.Win.Services.Auth;

public sealed class SerialManager
{
    private static readonly Regex Pattern = new("^[A-Z0-9]{3,4}-[A-Z0-9]{3,4}-[A-Z0-9]{3,4}$", RegexOptions.Compiled);

    private readonly string _statePath;
    private readonly ApiService _apiService;

    private sealed class SerialState
    {
        public string? SerialNumber { get; set; }
        public string? DeviceId { get; set; }
    }

    public SerialManager(ApiService? apiService = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BroadmeWin");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "serial_state.json");
        _apiService = apiService ?? new ApiService(new HttpClient());
    }

    public string? SavedSerialNumber
    {
        get
        {
            if (!File.Exists(_statePath)) return null;
            try
            {
                var json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<SerialState>(json)?.SerialNumber;
            }
            catch
            {
                return null;
            }
        }
    }

    public string DeviceId
    {
        get
        {
            var state = ReadState();
            if (!string.IsNullOrWhiteSpace(state?.DeviceId)) return state!.DeviceId!;

            var generated = GenerateDeviceId();
            SaveState(new SerialState
            {
                SerialNumber = state?.SerialNumber,
                DeviceId = generated
            });
            return generated;
        }
    }

    public bool IsValidFormat(string serial)
    {
        var value = serial.Trim().ToUpperInvariant();
        return value.Length >= 8 && Pattern.IsMatch(value);
    }

    public async Task<(bool IsValid, string Message)> ValidateAndBindAsync(string serial, CancellationToken ct = default)
    {
        var value = serial.Trim().ToUpperInvariant();
        if (!IsValidFormat(value))
        {
            return (false, "序號格式錯誤,請檢查後重試");
        }

        try
        {
            var result = await _apiService.ValidateSerialAsync(value, DeviceId, ct);
            if (result.validation)
            {
                SaveSerial(value);
                return (true, string.IsNullOrWhiteSpace(result.message) ? "序號綁定成功" : result.message);
            }

            return (false, string.IsNullOrWhiteSpace(result.message) ? "序號驗證失敗" : result.message);
        }
        catch
        {
            return (false, "連線失敗,請檢查網路後重試");
        }
    }

    public async Task<(bool IsValid, string Message)> RevalidateSavedSerialAsync(CancellationToken ct = default)
    {
        var serial = SavedSerialNumber;
        if (string.IsNullOrWhiteSpace(serial)) return (false, "尚未綁定序號");

        try
        {
            var result = await _apiService.ValidateSerialAsync(serial, DeviceId, ct);
            if (result.validation)
            {
                return (true, string.IsNullOrWhiteSpace(result.message) ? "序號驗證成功" : result.message);
            }

            return (false, string.IsNullOrWhiteSpace(result.message) ? "序號已失效" : result.message);
        }
        catch
        {
            // 對齊 mac 版策略：網路失敗時可選擇信任本地序號
            return (true, "離線模式：暫時信任本地序號");
        }
    }

    public void SaveSerial(string serial)
    {
        var value = serial.Trim().ToUpperInvariant();
        var state = ReadState() ?? new SerialState();
        state.SerialNumber = value;
        if (string.IsNullOrWhiteSpace(state.DeviceId)) state.DeviceId = GenerateDeviceId();
        SaveState(state);
    }

    public void ClearSerial()
    {
        if (File.Exists(_statePath)) File.Delete(_statePath);
    }

    private SerialState? ReadState()
    {
        if (!File.Exists(_statePath)) return null;
        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<SerialState>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SaveState(SerialState state)
    {
        var payload = JsonSerializer.Serialize(state);
        File.WriteAllText(_statePath, payload);
    }

    private static string GenerateDeviceId()
    {
        try
        {
            var mac = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress()?.ToString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (!string.IsNullOrWhiteSpace(mac))
            {
                return $"MAC-{mac[..Math.Min(mac.Length, 12)]}";
            }
        }
        catch
        {
            // ignore and fallback
        }

        return $"UUID-{Guid.NewGuid().ToString("N")[..12]}";
    }
}
