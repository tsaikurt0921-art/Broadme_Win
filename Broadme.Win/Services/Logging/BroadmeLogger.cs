namespace Broadme.Win.Services.Logging;

public sealed class BroadmeLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BroadmeLogger()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _path = Path.Combine(dir, "Broadme_Performance_Win.log");
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
