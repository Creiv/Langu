using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Langu.Core;
using Langu.Core.Native;

namespace Langu.Overlay;

public partial class OverlayWindow : Window, IOverlayController
{
    private readonly DispatcherTimer _hudPulse = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private double _hudPhase;
    private bool _hudWorking;
    private ScreenRect _lastBounds;
    private ScreenRect _focusScreen;
    private bool _focusSelecting;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        _hudPulse.Tick += (_, _) => PulseHud();
    }

    public void ShowOverlay()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowOverlay);
            return;
        }
        if (!IsVisible)
            Show();
        ApplyWin32Styles();
    }

    public void HideOverlay()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(HideOverlay);
            return;
        }
        if (IsVisible)
            Hide();
    }

    public void Update(IReadOnlyList<OverlayItem> items, ScreenRect overlayBounds)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Update(items, overlayBounds));
            return;
        }
        if (overlayBounds.IsEmpty)
        {
            RootCanvas.Children.Clear();
            if (HudRoot.Visibility != Visibility.Visible && FocusBand.Visibility != Visibility.Visible)
                HideOverlay();
            return;
        }

        _lastBounds = overlayBounds;
        ShowOverlay();
        PlaceOnScreen(overlayBounds);
        Rebuild(items, overlayBounds);
        ApplyFocusBand();
    }

    public void SetHud(OcrHudState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetHud(state));
            return;
        }

        if (!state.Visible)
        {
            HudRoot.Visibility = Visibility.Collapsed;
            _hudPulse.Stop();
            _hudWorking = false;
            if (RootCanvas.Children.Count == 0 && FocusBand.Visibility != Visibility.Visible)
                HideOverlay();
            return;
        }

        if (_lastBounds.IsEmpty)
            return;

        ShowOverlay();
        PlaceOnScreen(_lastBounds);
        HudRoot.Visibility = Visibility.Visible;
        HudText.Text = state.Text;
        _hudWorking = state.Working;
        if (state.Working)
        {
            HudRoot.Background = new SolidColorBrush(Color.FromArgb(0xEE, 18, 22, 32));
            HudRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 139, 255));
            HudRoot.BorderThickness = new Thickness(1);
            HudDot.Fill = new SolidColorBrush(Color.FromRgb(61, 139, 255));
            if (!_hudPulse.IsEnabled)
                _hudPulse.Start();
        }
        else if (state.IsError)
        {
            _hudPulse.Stop();
            HudRoot.Background = new SolidColorBrush(Color.FromArgb(0xEE, 42, 22, 18));
            HudRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 80));
            HudRoot.BorderThickness = new Thickness(1);
            HudDot.Fill = new SolidColorBrush(Color.FromRgb(255, 140, 80));
            HudDot.Opacity = 1;
        }
        else
        {
            _hudPulse.Stop();
            HudRoot.Background = new SolidColorBrush(Color.FromArgb(0xEE, 16, 36, 28));
            HudRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 220, 151));
            HudRoot.BorderThickness = new Thickness(1);
            HudDot.Fill = new SolidColorBrush(Color.FromRgb(61, 220, 151));
            HudDot.Opacity = 1;
        }
    }

    public void SetFocusBand(ScreenRect rect, bool selecting)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetFocusBand(rect, selecting));
            return;
        }

        _focusScreen = rect;
        _focusSelecting = selecting;
        if (_lastBounds.IsEmpty && !rect.IsEmpty)
            return;
        if (!rect.IsEmpty && _lastBounds.IsEmpty)
            return;
        if (!rect.IsEmpty)
        {
            ShowOverlay();
            PlaceOnScreen(_lastBounds);
        }

        ApplyFocusBand();
        if (rect.IsEmpty
            && HudRoot.Visibility != Visibility.Visible
            && RootCanvas.Children.Count == 0)
            HideOverlay();
    }

    private void ApplyFocusBand()
    {
        if (_focusScreen.IsEmpty || _lastBounds.IsEmpty)
        {
            FocusBand.Visibility = Visibility.Collapsed;
            return;
        }

        var dipW = ActualWidth > 1 ? ActualWidth : Width;
        var dipH = ActualHeight > 1 ? ActualHeight : Height;
        var sx = _lastBounds.Width > 0 ? dipW / _lastBounds.Width : 1;
        var sy = _lastBounds.Height > 0 ? dipH / _lastBounds.Height : 1;
        var x = (_focusScreen.X - _lastBounds.X) * sx;
        var y = (_focusScreen.Y - _lastBounds.Y) * sy;
        var w = Math.Max(2, _focusScreen.Width * sx);
        var h = Math.Max(2, _focusScreen.Height * sy);
        Canvas.SetLeft(FocusBand, Math.Round(x));
        Canvas.SetTop(FocusBand, Math.Round(y));
        FocusBand.Width = w;
        FocusBand.Height = h;
        FocusBand.Stroke = new SolidColorBrush(Color.FromRgb(90, 170, 255));
        FocusBand.Fill = new SolidColorBrush(_focusSelecting
            ? Color.FromArgb(0x40, 61, 139, 255)
            : Color.FromArgb(0x1C, 61, 139, 255));
        FocusBand.StrokeDashArray = _focusSelecting ? new DoubleCollection { 5, 4 } : null;
        FocusBand.Visibility = Visibility.Visible;
    }

    private void PulseHud()
    {
        if (!_hudWorking)
            return;
        _hudPhase += 0.22;
        HudDot.Opacity = 0.28 + 0.72 * (0.5 + 0.5 * Math.Sin(_hudPhase));
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyWin32Styles();

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyWin32Styles();

    private void ApplyWin32Styles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var ex = User32.GetWindowLong(hwnd, NativeConstants.GwlExStyle);
        ex |= NativeConstants.WsExLayered
              | NativeConstants.WsExTransparent
              | NativeConstants.WsExNoActivate
              | NativeConstants.WsExToolWindow
              | NativeConstants.WsExTopmost;
        User32.SetWindowLong(hwnd, NativeConstants.GwlExStyle, ex);
        User32.SetWindowDisplayAffinity(hwnd, NativeConstants.WdaExcludeFromCapture);
    }

    private void PlaceOnScreen(ScreenRect bounds)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyWin32Styles();

        var toDip = GetToDip();
        var topLeft = toDip.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = toDip.Transform(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);

        User32.SetWindowPos(
            hwnd,
            new IntPtr(NativeConstants.HwndTopmost),
            bounds.X,
            bounds.Y,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            NativeConstants.SwpNoActivate | NativeConstants.SwpShowWindow);
        UpdateLayout();
    }

    private Matrix GetToDip()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = PresentationSource.FromVisual(this) ?? (hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd) : null);
        if (source?.CompositionTarget is { } target)
            return target.TransformFromDevice;

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Matrix(1 / Math.Max(0.25, dpi.DpiScaleX), 0, 0, 1 / Math.Max(0.25, dpi.DpiScaleY), 0, 0);
    }

    private void Rebuild(IReadOnlyList<OverlayItem> items, ScreenRect overlayBounds)
    {
        RootCanvas.Children.Clear();
        var toDip = GetToDip();
        var origin = toDip.Transform(new Point(overlayBounds.X, overlayBounds.Y));

        foreach (var item in items)
        {
            if (!item.Shape.IsTilted)
            {
                var box = MapRect(item.ScreenBounds, toDip, origin);
                RootCanvas.Children.Add(BuildBox(item, box.X, box.Y, box.W, box.H, 0));
                continue;
            }

            var a = toDip.Transform(new Point(item.Shape.P0.X, item.Shape.P0.Y));
            var b = toDip.Transform(new Point(item.Shape.P1.X, item.Shape.P1.Y));
            var d = toDip.Transform(new Point(item.Shape.P3.X, item.Shape.P3.Y));
            var length = Math.Max(4, Dist(a, b));
            var thick = Math.Max(4, Dist(a, d));
            var cx = (toDip.Transform(new Point(item.Shape.CenterX, item.Shape.CenterY)).X) - origin.X;
            var cy = (toDip.Transform(new Point(item.Shape.CenterX, item.Shape.CenterY)).Y) - origin.Y;
            RootCanvas.Children.Add(BuildBox(
                item,
                cx - length / 2,
                cy - thick / 2,
                length,
                thick,
                item.Shape.AngleDegrees));
        }
    }

    private static (double X, double Y, double W, double H) MapRect(ScreenRect rect, Matrix toDip, Point origin)
    {
        var p = toDip.Transform(new Point(rect.X, rect.Y));
        var q = toDip.Transform(new Point(rect.X + rect.Width, rect.Y + rect.Height));
        return (
            Math.Floor(p.X - origin.X),
            Math.Floor(p.Y - origin.Y),
            Math.Max(4, Math.Ceiling(q.X - p.X)),
            Math.Max(4, Math.Ceiling(q.Y - p.Y)));
    }

    private static double Dist(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private UIElement BuildBox(OverlayItem item, double x, double y, double w, double h, double rotate)
    {
        var probe = item.Kind is OverlayItemKind.Probe or OverlayItemKind.Busy;
        var fill = probe
            ? HighlightFill(item)
            : Color.FromRgb(item.Appearance.FillR, item.Appearance.FillG, item.Appearance.FillB);
        UIElement? child = item.Kind switch
        {
            OverlayItemKind.Probe => null,
            OverlayItemKind.Busy => BuildBusyMark(item.Appearance, w, h),
            _ => FitLabel(
                string.IsNullOrWhiteSpace(item.TranslatedText) ? item.SourceText : item.TranslatedText,
                item.Appearance,
                w,
                h,
                item)
        };

        var root = new Grid
        {
            Width = w,
            Height = h,
            Background = new SolidColorBrush(fill),
            ClipToBounds = true,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = Math.Abs(rotate) >= 1 ? new RotateTransform(rotate) : Transform.Identity
        };
        if (item.Highlighted && item.Kind == OverlayItemKind.Translated)
        {
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(
                    0x46, item.Appearance.TextR, item.Appearance.TextG, item.Appearance.TextB))
            });
        }

        if (child is not null)
            root.Children.Add(child);
        Canvas.SetLeft(root, Math.Round(x));
        Canvas.SetTop(root, Math.Round(y));
        return root;
    }

    private static Color HighlightFill(OverlayItem item)
    {
        var look = item.Appearance;
        var strong = item.Highlighted || item.Kind == OverlayItemKind.Busy;
        var alpha = strong ? (byte)0x8C : (byte)0x32;
        return Color.FromArgb(alpha, look.TextR, look.TextG, look.TextB);
    }

    private static UIElement BuildBusyMark(TextAppearance look, double boxW, double boxH)
    {
        var ink = new SolidColorBrush(Color.FromRgb(look.TextR, look.TextG, look.TextB));
        var size = Math.Clamp(Math.Min(boxW, boxH) * 0.18, 6, 12);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Margin = new Thickness(size * 0.28, 0, size * 0.28, 0),
                Fill = ink,
                Opacity = 0.2
            };
            var pulse = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(320))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 140)
            };
            dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            row.Children.Add(dot);
        }

        return row;
    }

    private static UIElement FitLabel(string value, TextAppearance look, double boxW, double boxH, OverlayItem item)
    {
        var display = value.Replace("\r\n", "\n").Trim();
        var asian = LanguageDetector.HasCjk(display)
                    || LanguageDetector.HasCjk(item.SourceText) && !LanguageDetector.IsMostlyLatin(display);
        var banner = look.Vertical && boxW < boxH * 0.38 && boxW < 52;
        if (banner && LanguageDetector.HasCjk(display) && !display.Contains('\n') && display.Length is > 1 and <= 18)
            display = string.Join('\n', display.EnumerateRunes().Select(r => r.ToString()));

        var sourceLines = item.SourceText.Replace("\r\n", "\n").Count(c => c == '\n') + 1;
        var wrap = (display.Contains('\n') || sourceLines >= 2) && boxH >= 28;
        var fontSize = Math.Clamp(boxH * 0.86, 7, 96);
        var label = new TextBlock
        {
            Text = display,
            Foreground = new SolidColorBrush(Color.FromRgb(look.TextR, look.TextG, look.TextB)),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Meiryo UI"),
            FontSize = fontSize,
            FontWeight = look.Bold ? FontWeights.SemiBold : FontWeights.Regular,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextAlignment = asian || banner ? TextAlignment.Center : TextAlignment.Left,
            TextTrimming = TextTrimming.None,
            Width = wrap ? Math.Max(4, boxW) : double.NaN,
            IsHitTestVisible = false
        };

        return new Viewbox
        {
            Width = boxW,
            Height = boxH,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Child = label,
            IsHitTestVisible = false
        };
    }
}
