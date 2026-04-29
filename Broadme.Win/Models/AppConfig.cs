namespace Broadme.Win.Models;

public sealed class AppConfig
{
    public int StreamPort { get; set; } = 8080;
    public string BindIp { get; set; } = "0.0.0.0";
    public int SelectedFps { get; set; } = 15;
    public string SelectedResolution { get; set; } = "1280x720";
}
