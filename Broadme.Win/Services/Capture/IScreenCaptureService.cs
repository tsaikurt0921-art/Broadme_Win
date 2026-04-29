namespace Broadme.Win.Services.Capture;

public interface IScreenCaptureService
{
    Task StartAsync(int fps, string resolution, Func<byte[], (int width, int height), Task> onFrame, CancellationToken ct);
    Task StopAsync();
}
