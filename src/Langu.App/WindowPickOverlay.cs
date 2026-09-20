using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Langu.Core;

namespace Langu.App;

public sealed class WindowPickOverlay : System.Windows.Window
{
    private readonly TaskCompletionSource<WindowInfo?> _completion = new();

    private WindowPickOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 40, 80));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        WindowState = WindowState.Maximized;
        Content = new System.Windows.Controls.TextBlock
        {
            Text = "Click the window to translate  ·  Esc to cancel",
            Foreground = Brushes.White,
            FontSize = 22,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0)
        };
        MouseLeftButtonDown += OnClick;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Complete(null);
        };
    }

    public static Task<WindowInfo?> PickAsync()
    {
        var window = new WindowPickOverlay();
        window.Show();
        window.Activate();
        return window._completion.Task;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        var screen = PointToScreen(e.GetPosition(this));
        Complete(WindowEnumeration.FromPoint((int)screen.X, (int)screen.Y));
    }

    private void Complete(WindowInfo? info)
    {
        if (_completion.Task.IsCompleted)
            return;
        Close();
        _completion.TrySetResult(info);
    }
}
