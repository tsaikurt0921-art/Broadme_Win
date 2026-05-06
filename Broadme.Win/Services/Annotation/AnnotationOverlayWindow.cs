using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;

namespace Broadme.Win.Services.Annotation;

public sealed class AnnotationOverlayWindow : Window
{
    private static AnnotationOverlayWindow? _instance;

    private readonly System.Windows.Controls.Canvas _canvas;
    private Polyline? _currentStroke;
    private readonly List<Polyline> _strokeHistory = new();
    private readonly Stack<Polyline> _redoStack = new();

    private AnnotationOverlayWindow()
    {
        var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? Forms.SystemInformation.VirtualScreen;

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;

        _canvas = new System.Windows.Controls.Canvas
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = System.Windows.Media.Brushes.Transparent,
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

    public void StartStroke(WpfPoint point, string colorHex = "#ef4444", double width = 4)
    {
        Dispatcher.Invoke(() =>
        {
            ShowOverlay();

            var brush = (SolidColorBrush?)new BrushConverter().ConvertFromString(colorHex) ?? System.Windows.Media.Brushes.Red;
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
            _redoStack.Clear();
        });
    }

    public void MoveStroke(WpfPoint point)
    {
        Dispatcher.Invoke(() =>
        {
            _currentStroke?.Points.Add(point);
        });
    }

    public void EndStroke(WpfPoint point)
    {
        Dispatcher.Invoke(() =>
        {
            _currentStroke?.Points.Add(point);
            if (_currentStroke is not null && !_strokeHistory.Contains(_currentStroke))
            {
                _strokeHistory.Add(_currentStroke);
            }
            _currentStroke = null;
        });
    }

    public void ClearAll()
    {
        Dispatcher.Invoke(() =>
        {
            _canvas.Children.Clear();
            _strokeHistory.Clear();
            _redoStack.Clear();
            _currentStroke = null;
            HideOverlay();
        });
    }

    public void UndoStroke()
    {
        Dispatcher.Invoke(() =>
        {
            if (_strokeHistory.Count == 0) return;
            var last = _strokeHistory[^1];
            _strokeHistory.RemoveAt(_strokeHistory.Count - 1);
            _canvas.Children.Remove(last);
            _redoStack.Push(last);
            if (_canvas.Children.Count == 0) HideOverlay();
        });
    }

    public void RedoStroke()
    {
        Dispatcher.Invoke(() =>
        {
            if (_redoStack.Count == 0) return;
            var stroke = _redoStack.Pop();
            _canvas.Children.Add(stroke);
            _strokeHistory.Add(stroke);
            ShowOverlay();
        });
    }
}
