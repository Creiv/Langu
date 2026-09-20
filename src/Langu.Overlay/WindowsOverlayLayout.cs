using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Langu.Core;

namespace Langu.Overlay;

internal static class WindowsOverlayLayout
{
    public static Layout Measure(
        string text,
        string source,
        double boxW,
        double boxH,
        TextAppearance look,
        double pixelsPerDip,
        bool bold)
    {
        var display = text.Replace("\r\n", "\n").Trim();
        var fit = OverlayFont.FitWindows(boxH, source, display);
        var font = fit.FontSize;
        FormattedText ft = Create(display, font, look, pixelsPerDip, bold);
        for (var i = 0; i < 8; i++)
        {
            var body = ft.Baseline;
            var ink = Math.Max(ft.Extent, ft.OverhangAfter + ft.Baseline);
            var tooTall = fit.FullLineInBox ? ink > boxH + 1.5 : body > boxH + 0.6;
            if (!tooTall || font <= 11)
                break;
            font = Math.Max(11, font * 0.93);
            ft = Create(display, font, look, pixelsPerDip, bold);
        }

        var top = fit.FullLineInBox
            ? Math.Round((boxH - ft.Extent) / 2)
            : Math.Round(boxH - ft.Baseline);
        var width = Math.Max(boxW, Math.Ceiling(ft.WidthIncludingTrailingWhitespace + 4));
        var left = fit.Center ? Math.Round((width - ft.Width) / 2) : 0;
        return new Layout(ft, top, left, width, fit.Center);
    }

    private static FormattedText Create(
        string text,
        double fontSize,
        TextAppearance look,
        double pixelsPerDip,
        bool bold)
    {
        var typeface = new Typeface(
            new FontFamily("Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Meiryo UI"),
            FontStyles.Normal,
            bold ? FontWeights.SemiBold : FontWeights.Regular,
            FontStretches.Normal);
        return new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(Color.FromRgb(look.TextR, look.TextG, look.TextB)),
            pixelsPerDip);
    }

    internal readonly record struct Layout(
        FormattedText Text,
        double Top,
        double Left,
        double Width,
        bool Center);
}

internal sealed class OverlayGlyphs : FrameworkElement
{
    public OverlayGlyphs(FormattedText text)
    {
        Text = text;
        Width = Math.Ceiling(text.WidthIncludingTrailingWhitespace + 2);
        Height = Math.Ceiling(Math.Max(text.Height, text.Extent) + 4);
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public FormattedText Text { get; }

    protected override void OnRender(DrawingContext dc) =>
        dc.DrawText(Text, new Point(0, 0));
}
