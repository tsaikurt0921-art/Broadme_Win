using System.IO;

namespace Broadme.Win.Services.Logging;

public sealed class BroadmeLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BroadmeLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BroadmeWin",
            "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "broadme.log");
    }

    public async Task LogAsync(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        await _gate.WaitAsync();
        try { await File.AppendAllTextAsync(_path, line); }
        finally { _gate.Release(); }
    }

    public string GetLogPath() => _path;
}
