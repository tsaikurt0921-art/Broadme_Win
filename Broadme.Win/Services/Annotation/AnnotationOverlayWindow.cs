using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace Broadme.Win.Services.Annotation;

public sealed class AnnotationOverlayWindow : Window
{
    private static AnnotationOverlayWindow? _instance;

    private readonly System.Windows.Controls.Canvas _canvas;
    private Polyline? _currentStroke;

    private AnnotationOverlayWindow()
    {
        var bounds = Forms.SystemInformation.VirtualScreen;

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;

        _canvas = new System.Windows.Controls.Canvas
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        Content = _canvas;
    }

    public static AnnotationOverlayWindow Instance => _instance ??= new AnnotationOverlayWindow();

    public static void ShowOverlay()
    {
        var overlay = Instance;
        if (!overlay.IsVisible)
        {
            overlay.Show();
        }
    }

    public static void HideOverlay()
    {
        if (_instance is { IsVisible: true })
        {
            _instance.Hide();
        }
    }

    public void StartStroke(Point point, string colorHex = "#ef4444", double width = 4)
    {
        Dispatcher.Invoke(() =>
        {
            ShowOverlay();

            var brush = (SolidColorBrush?)new BrushConverter().ConvertFromString(colorHex) ?? Brushes.Red;
            _currentStroke = new Polyline
            {
                Stroke = brush,
                StrokeThickness = width,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true
            };
            _currentStroke.Points.Add(point);
            _canvas.Children.Add(_currentStroke);
        });
    }

    public void MoveStroke(Point point)
    {
        Dispatcher.Invoke(() =>
        {
            _currentStroke?.Points.Add(point);
        });
    }

    public void EndStroke(Point point)
    {
        Dispatcher.Invoke(() =>
        {
            _currentStroke?.Points.Add(point);
            _currentStroke = null;
        });
    }

    public void ClearAll()
    {
        Dispatcher.Invoke(() =>
        {
            _canvas.Children.Clear();
            _currentStroke = null;
            HideOverlay();
        });
    }
}
