using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Langu.Core;

namespace Langu.App;

public sealed class RegionPickOverlay : System.Windows.Window
{
    private readonly TaskCompletionSource<ScreenRect?> _completion = new();
    private readonly Canvas _canvas = new();
    private readonly System.Windows.Shapes.Rectangle _rect = new()
    {
        Stroke = Brushes.White,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x55, 61, 139, 255))
    };
    private System.Windows.Point _start;
    private bool _dragging;

    private RegionPickOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        WindowState = WindowState.Maximized;
        _canvas.Children.Add(_rect);
        var hint = new TextBlock
        {
            Text = "Trascina per selezionare la regione  ·  Esc per annullare",
            Foreground = Brushes.White,
            FontSize = 22,
            Margin = new Thickness(24, 32, 0, 0)
        };
        _canvas.Children.Add(hint);
        Content = _canvas;

        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _start = e.GetPosition(_canvas);
            CaptureMouse();
        };
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Complete(null);
        };
    }

    public static Task<ScreenRect?> PickAsync()
    {
        var window = new RegionPickOverlay();
        window.Show();
        window.Activate();
        return window._completion.Task;
    }

    private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
            return;
        var pos = e.GetPosition(_canvas);
        var x = Math.Min(_start.X, pos.X);
        var y = Math.Min(_start.Y, pos.Y);
        _rect.Width = Math.Abs(pos.X - _start.X);
        _rect.Height = Math.Abs(pos.Y - _start.Y);
        Canvas.SetLeft(_rect, x);
        Canvas.SetTop(_rect, y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        ReleaseMouseCapture();
        var pos = e.GetPosition(_canvas);
        var p1 = PointToScreen(_start);
        var p2 = PointToScreen(pos);
        var x = (int)Math.Min(p1.X, p2.X);
        var y = (int)Math.Min(p1.Y, p2.Y);
        var w = (int)Math.Abs(p2.X - p1.X);
        var h = (int)Math.Abs(p2.Y - p1.Y);
        Complete(w < 20 || h < 20 ? null : new ScreenRect(x, y, w, h));
    }

    private void Complete(ScreenRect? rect)
    {
        if (_completion.Task.IsCompleted)
            return;
        Close();
        _completion.TrySetResult(rect);
    }
}
