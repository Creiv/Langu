namespace Langu.Core;

public sealed class CapturedFrame
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required ScreenRect ScreenBounds { get; init; }

    public double ScaleX => ScreenBounds.Width <= 0 ? 1 : ScreenBounds.Width / (double)Math.Max(1, Width);
    public double ScaleY => ScreenBounds.Height <= 0 ? 1 : ScreenBounds.Height / (double)Math.Max(1, Height);

    public bool PixelAligned =>
        Math.Abs(ScreenBounds.Width - Width) <= 2 && Math.Abs(ScreenBounds.Height - Height) <= 2;

    public CapturedFrame WithBounds(ScreenRect bounds) => new()
    {
        Bgra = Bgra,
        Width = Width,
        Height = Height,
        Stride = Stride,
        ScreenBounds = bounds
    };

    public ScreenRect MapToScreen(ScreenRect imageBox)
    {
        var a = MapToScreen(new ScreenPoint(imageBox.X, imageBox.Y));
        var b = MapToScreen(new ScreenPoint(imageBox.X + imageBox.Width, imageBox.Y + imageBox.Height));
        return new ScreenRect(a.X, a.Y, Math.Max(1, b.X - a.X), Math.Max(1, b.Y - a.Y));
    }

    public ScreenPoint MapToScreen(ScreenPoint point)
    {
        if (PixelAligned)
            return new ScreenPoint(ScreenBounds.X + point.X, ScreenBounds.Y + point.Y);
        return new ScreenPoint(
            ScreenBounds.X + (int)Math.Round(point.X * ScaleX, MidpointRounding.AwayFromZero),
            ScreenBounds.Y + (int)Math.Round(point.Y * ScaleY, MidpointRounding.AwayFromZero));
    }

    public TextQuad MapToScreen(TextQuad quad) =>
        quad.IsValid ? quad.Map(MapToScreen) : TextQuad.FromRect(MapToScreen(quad.Bounds));
}

public static class ScreenMapping
{
    public static ScreenRect Align(ScreenRect visible, int imageWidth, int imageHeight)
    {
        if (visible.IsEmpty || imageWidth < 1 || imageHeight < 1)
            return visible;
        if (Math.Abs(visible.Width - imageWidth) <= 4 && Math.Abs(visible.Height - imageHeight) <= 4)
            return new ScreenRect(visible.X, visible.Y, imageWidth, imageHeight);
        return visible;
    }

    public static ScreenRect ForCapture(ScreenRect origin, int imageWidth, int imageHeight)
    {
        if (origin.IsEmpty)
            return origin;
        if (imageWidth < 1 || imageHeight < 1)
            return origin;
        if (Math.Abs(origin.Width - imageWidth) <= 2 && Math.Abs(origin.Height - imageHeight) <= 2)
            return new ScreenRect(origin.X, origin.Y, imageWidth, imageHeight);
        return origin;
    }
}

public sealed class OcrLine
{
    public required string Text { get; init; }
    public required ScreenRect Bounds { get; init; }
    public TextQuad Quad { get; init; }
    public float Confidence { get; init; }
    public TextQuad Shape => Quad.IsValid ? Quad : TextQuad.FromRect(Bounds);
}

public sealed class TranslatedLine
{
    public required string SourceText { get; init; }
    public required string TranslatedText { get; init; }
    public required ScreenRect ScreenBounds { get; init; }
    public required string SourceLanguage { get; init; }
    public TextAppearance Appearance { get; init; } = TextAppearance.Fallback(ScreenRect.Empty);
}

public sealed record OverlayItem
{
    public required string Id { get; init; }
    public required string SourceText { get; init; }
    public string TranslatedText { get; init; } = "";
    public required ScreenRect ScreenBounds { get; init; }
    public TextQuad Quad { get; init; }
    public required string SourceLanguage { get; init; }
    public TextAppearance Appearance { get; init; } = TextAppearance.Fallback(ScreenRect.Empty);
    public OverlayItemKind Kind { get; init; } = OverlayItemKind.Probe;
    public TextQuad Shape => Quad.IsValid ? Quad : TextQuad.FromRect(ScreenBounds);
}

public sealed class WindowInfo
{
    public required long Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public required ScreenRect Bounds { get; init; }
    public bool IsMinimized { get; init; }
    public bool LooksFullscreen { get; init; }
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{ProcessName}  —  {Title}";
}

public sealed class MonitorInfo
{
    public required int Index { get; init; }
    public required long Handle { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required bool IsPrimary { get; init; }
    public string DisplayName => IsPrimary
        ? $"Monitor {Index + 1} (principale) {Bounds.Width}×{Bounds.Height}"
        : $"Monitor {Index + 1} {Bounds.Width}×{Bounds.Height}";
}

public sealed class PipelineStatus
{
    public bool Running { get; set; }
    public bool ModelsReady { get; set; }
    public string Message { get; set; } = "In attesa";
    public string? Warning { get; set; }
    public int LastOcrCount { get; set; }
    public int LastTranslatedCount { get; set; }
    public double LastFrameMs { get; set; }
}

public sealed class OcrHudState
{
    public static OcrHudState Hidden { get; } = new();
    public bool Visible { get; init; }
    public bool Working { get; init; }
    public string Text { get; init; } = "";

    public bool IsError { get; init; }

    public static OcrHudState Busy(string text) => new() { Visible = true, Working = true, Text = text };
    public static OcrHudState Ready(string text) => new() { Visible = true, Working = false, Text = text };
    public static OcrHudState Error(string text) => new() { Visible = true, Working = false, IsError = true, Text = text };
}
