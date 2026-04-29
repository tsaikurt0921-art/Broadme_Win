using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Broadme.Win.Services.Capture;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    private CancellationTokenSource? _cts;

    public Task StartAsync(int fps, string resolution, Func<byte[], (int width, int height), Task> onFrame, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        var frameInterval = TimeSpan.FromMilliseconds(Math.Max(10, 1000 / Math.Max(1, fps)));
        var targetSize = ParseResolution(resolution);

        _ = Task.Run(async () =>
        {
            var screen = Screen.PrimaryScreen;
            if (screen is null) return;

            var sourceBounds = screen.Bounds;
            var sourceSize = new Size(sourceBounds.Width, sourceBounds.Height);

            // 若未指定或格式錯誤，維持原生解析度
            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                targetSize = sourceSize;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var sourceBmp = new Bitmap(sourceSize.Width, sourceSize.Height, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(sourceBmp))
                    {
                        g.CopyFromScreen(sourceBounds.Left, sourceBounds.Top, 0, 0, sourceSize, CopyPixelOperation.SourceCopy);
                    }

                    using var outputBmp = ResizeWithLetterbox(sourceBmp, targetSize);
                    using var ms = new MemoryStream();

                    // 參照 mac 版偏低品質設計，優先穩定與頻寬
                    SaveJpeg(outputBmp, ms, quality: 55L);

                    await onFrame(ms.ToArray(), (outputBmp.Width, outputBmp.Height));
                    await Task.Delay(frameInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 避免單次擷取錯誤中斷整體串流
                    await Task.Delay(50, token);
                }
            }
        }, token);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    private static Size ParseResolution(string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return Size.Empty;

        var raw = resolution.Trim();
        // 支援 "1280x720" 與 "1280x720 (HD)"
        var token = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = token.Split('x', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2) return Size.Empty;
        if (!int.TryParse(parts[0], out var w)) return Size.Empty;
        if (!int.TryParse(parts[1], out var h)) return Size.Empty;

        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    private static Bitmap ResizeWithLetterbox(Bitmap source, Size target)
    {
        var canvas = new Bitmap(target.Width, target.Height, PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(canvas);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Black);

        var scale = Math.Min((double)target.Width / source.Width, (double)target.Height / source.Height);
        var drawW = (int)Math.Round(source.Width * scale);
        var drawH = (int)Math.Round(source.Height * scale);
        var offsetX = (target.Width - drawW) / 2;
        var offsetY = (target.Height - drawH) / 2;

        g.DrawImage(source, new Rectangle(offsetX, offsetY, drawW, drawH));
        return canvas;
    }

    private static void SaveJpeg(Bitmap bitmap, Stream stream, long quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder is null)
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100));
        bitmap.Save(stream, encoder, parameters);
    }
}
