using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broadme.Win.Services.Auth;

public sealed class SerialManager
{
    private static readonly Regex Pattern = new("^[A-Z0-9]{3,4}-[A-Z0-9]{3,4}-[A-Z0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromDays(7);

    private readonly string _statePath;
    private readonly ApiService _apiService;

    private sealed class SerialState
    {
        public string? SerialNumber { get; set; }
        public string? DeviceId { get; set; }
        public DateTimeOffset? LastValidatedAtUtc { get; set; }
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
            var state = ReadState();
            return state?.SerialNumber;
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
                DeviceId = generated,
                LastValidatedAtUtc = state?.LastValidatedAtUtc
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
                SaveSerial(value, DateTimeOffset.UtcNow);
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
                var state = ReadState() ?? new SerialState();
                state.SerialNumber = serial;
                if (string.IsNullOrWhiteSpace(state.DeviceId)) state.DeviceId = GenerateDeviceId();
                state.LastValidatedAtUtc = DateTimeOffset.UtcNow;
                SaveState(state);
                return (true, string.IsNullOrWhiteSpace(result.message) ? "序號驗證成功" : result.message);
            }

            return (false, string.IsNullOrWhiteSpace(result.message) ? "序號已失效" : result.message);
        }
        catch
        {
            var state = ReadState();
            if (state?.LastValidatedAtUtc is null)
            {
                return (false, "離線驗證失敗：沒有可用的最近驗證紀錄");
            }

            if (DateTimeOffset.UtcNow - state.LastValidatedAtUtc <= OfflineGracePeriod)
            {
                return (true, $"離線模式：使用最近 {OfflineGracePeriod.Days} 天內驗證紀錄");
            }

            return (false, "離線驗證已逾期，請連線網路重新驗證");
        }
    }

    public void SaveSerial(string serial, DateTimeOffset? lastValidatedAtUtc = null)
    {
        var value = serial.Trim().ToUpperInvariant();
        var state = ReadState() ?? new SerialState();
        state.SerialNumber = value;
        if (string.IsNullOrWhiteSpace(state.DeviceId)) state.DeviceId = GenerateDeviceId();
        state.LastValidatedAtUtc = lastValidatedAtUtc ?? state.LastValidatedAtUtc;
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
            var plainBytes = Unprotect(File.ReadAllBytes(_statePath));
            var json = Encoding.UTF8.GetString(plainBytes);
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
        var encrypted = Protect(Encoding.UTF8.GetBytes(payload));
        File.WriteAllBytes(_statePath, encrypted);
    }

    private static string GenerateDeviceId()
    {
        try
        {
            var macs = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress()?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();

            if (macs.Length > 0)
            {
                var raw = $"{string.Join("|", macs)}|{Environment.MachineName}|{Environment.OSVersion.VersionString}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
                return $"HW-{Convert.ToHexString(hash)[..16]}";
            }
        }
        catch
        {
            // ignore and fallback
        }

        return $"UUID-{Guid.NewGuid().ToString("N")[..12]}";
    }

    private static byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(byte[] data)
        => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
