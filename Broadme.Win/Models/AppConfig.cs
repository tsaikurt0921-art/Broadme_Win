using System.IO;
using System.Text.Json;

namespace Broadme.Win.Models;

public sealed class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BroadmeWin",
        "config.json");

    public int StreamPort { get; set; } = 8080;
    public string BindIp { get; set; } = "0.0.0.0";
    public int SelectedFps { get; set; } = 15;
    public string SelectedResolution { get; set; } = "1280x720";

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
