using System.Diagnostics;
using System.Security.Principal;

namespace Broadme.Win.Services.Networking;

public static class FirewallService
{
    private const string RuleName = "Broadme Stream Server";

    public static async Task<bool> EnsureFirewallRule(int port)
    {
        if (!IsWindows()) return false;

        try
        {
            // 1. 檢查規則是否已存在
            if (await RuleExists(port)) return true;

            // 2. 如果不存在，嘗試建立規則 (需要管理員權限)
            return await AddRule(port);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Firewall check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RuleExists(int port)
    {
        var command = $"advfirewall firewall show rule name=\"{RuleName}\"";
        var result = await RunNetshAsync(command);
        // 如果輸出的內容包含通訊埠號碼，則視為已存在
        return result.Contains(port.ToString()) && result.Contains("Allow");
    }

    private static async Task<bool> AddRule(int port)
    {
        if (!IsAdministrator())
        {
            Debug.WriteLine("Not an administrator, cannot add firewall rule automatically.");
            return false;
        }

        var command = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} profile=any description=\"Allow Broadme Screen Streaming and Control\"";
        var result = await RunNetshAsync(command);
        return result.Contains("Ok") || result.Contains("確定");
    }

    private static async Task<string> RunNetshAsync(string args)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(950) // 繁體中文 Windows 預設編碼
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsWindows() => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
}
