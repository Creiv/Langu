using Langu.Core;
using SkiaSharp;

namespace Langu.Ocr;

internal static class ImageEnhance
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public static (SKBitmap Bitmap, float Scale) ForOcr(SKBitmap source) =>
        Scale(source, GameScale(source.Width, source.Height), contrast: false, nearest: true);

    public static (SKBitmap Bitmap, float Scale) Scale(SKBitmap source, float scale, bool contrast) =>
        Scale(source, scale, contrast, nearest: true);

    public static (SKBitmap Bitmap, float Scale) Scale(SKBitmap source, float scale, bool contrast, bool nearest = true)
    {
        scale = Math.Max(1f, scale);
        var width = Math.Max(8, (int)Math.Round(source.Width * scale));
        var height = Math.Max(8, (int)Math.Round(source.Height * scale));
        var dest = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.Black);
            var sampling = nearest ? Nearest : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, SKRect.Create(source.Width, source.Height), SKRect.Create(width, height), sampling);
        }

        if (contrast)
            StretchContrast(dest, mild: true);
        return (dest, scale);
    }

    public static float GameScale(int width, int height)
    {
        var maxSide = Math.Max(width, height);
        if (maxSide < 8)
            return 2f;
        var scale = 2.15f;
        if (maxSide * scale > 2880)
            scale = 2880f / maxSide;
        return Math.Clamp(scale, 1.45f, 2.6f);
    }

    public static SKBitmap Invert(SKBitmap source)
    {
        var dest = source.Copy() ?? throw new InvalidOperationException("Bitmap copy failed.");
        var n = dest.Width * dest.Height;
        var ptr = dest.GetPixels();
        if (ptr == IntPtr.Zero)
            return dest;
        unsafe
        {
            var p = (byte*)ptr;
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                p[o] = (byte)(255 - p[o]);
                p[o + 1] = (byte)(255 - p[o + 1]);
                p[o + 2] = (byte)(255 - p[o + 2]);
            }
        }
        return dest;
    }

    public static (SKBitmap Bitmap, float Scale) ForGameUi(SKBitmap source)
    {
        using var isolated = IsolateBrightText(source);
        return Scale(isolated, GameScale(source.Width, source.Height), contrast: false, nearest: true);
    }

    public static bool LooksLikeGameUi(SKBitmap source)
    {
        var n = source.Width * source.Height;
        var ptr = source.GetPixels();
        if (ptr == IntPtr.Zero || n < 80)
            return false;

        var step = Math.Max(1, n / 2200);
        var samples = 0;
        var bright = 0;
        var dark = 0;
        var sat = 0;
        long lumaSum = 0;
        unsafe
        {
            var p = (byte*)ptr;
            for (var i = 0; i < n; i += step)
            {
                var o = i * 4;
                var b = p[o];
                var g = p[o + 1];
                var r = p[o + 2];
                var luma = (r * 30 + g * 59 + b * 11) / 100;
                lumaSum += luma;
                samples++;
                if (luma >= 168)
                    bright++;
                if (luma <= 70)
                    dark++;
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= 36)
                    sat++;
            }
        }

        if (samples < 20)
            return false;
        var mean = lumaSum / (double)samples;
        return mean < 155
               && bright / (double)samples >= 0.012
               && dark / (double)samples >= 0.12
               && sat / (double)samples >= 0.18;
    }

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForLineRefine(SKBitmap source, ScreenRect box, bool isolateBright)
    {
        var crop = CropScale(source, box, padRatio: 12, minPad: 6, targetMin: 72, minScale: 2.8f, maxScale: 5.4f, cap: 1600, contrast: false);
        if (!isolateBright)
            return crop;

        var isolated = IsolateBrightText(crop.Bitmap);
        crop.Bitmap.Dispose();
        return (isolated, crop.Scale, crop.SourceBox);
    }

    public static SKBitmap IsolateBrightText(SKBitmap source)
    {
        var dest = source.Copy() ?? throw new InvalidOperationException("Bitmap copy failed.");
        var n = dest.Width * dest.Height;
        var ptr = dest.GetPixels();
        if (ptr == IntPtr.Zero)
            return dest;

        unsafe
        {
            var p = (byte*)ptr;
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                var luma = (p[o + 2] * 30 + p[o + 1] * 59 + p[o] * 11) / 100;
                var v = luma >= 158 ? (byte)255 : (byte)0;
                p[o] = v;
                p[o + 1] = v;
                p[o + 2] = v;
                p[o + 3] = 255;
            }
        }

        return dest;
    }

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForPaper(SKBitmap source, ScreenRect box) =>
        CropScale(source, box, padRatio: 18, minPad: 12, targetMin: 1400, minScale: 2.0f, maxScale: 3.8f, cap: 2400, contrast: false);

    public static SKBitmap Rotate(SKBitmap source, float degrees)
    {
        var rad = degrees * (MathF.PI / 180f);
        var cos = MathF.Abs(MathF.Cos(rad));
        var sin = MathF.Abs(MathF.Sin(rad));
        var width = Math.Max(8, (int)Math.Ceiling(source.Width * cos + source.Height * sin));
        var height = Math.Max(8, (int)Math.Ceiling(source.Width * sin + source.Height * cos));
        var dest = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dest);
        canvas.Clear(SKColors.White);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return dest;
    }

    public static ScreenPoint Unrotate(ScreenPoint point, int sourceWidth, int sourceHeight, int rotatedWidth, int rotatedHeight, float degrees)
    {
        var rad = -degrees * (Math.PI / 180);
        var x = point.X - rotatedWidth / 2.0;
        var y = point.Y - rotatedHeight / 2.0;
        var nx = x * Math.Cos(rad) - y * Math.Sin(rad);
        var ny = x * Math.Sin(rad) + y * Math.Cos(rad);
        return new ScreenPoint(
            (int)Math.Round(nx + sourceWidth / 2.0, MidpointRounding.AwayFromZero),
            (int)Math.Round(ny + sourceHeight / 2.0, MidpointRounding.AwayFromZero));
    }

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForTile(SKBitmap source, ScreenRect box) =>
        CropScale(source, box, padRatio: 40, minPad: 4, targetMin: 1200, minScale: 1.8f, maxScale: 3.2f, cap: 2200, contrast: false);

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForSmallBox(SKBitmap source, ScreenRect box)
    {
        var pad = Math.Max(8, Math.Max(box.Height / 2, 10));
        return CropScale(source, new ScreenRect(box.X - pad, box.Y - pad, box.Width + pad * 2, box.Height + pad * 2),
            padRatio: 80, minPad: 0, targetMin: 64, minScale: 2.4f, maxScale: 5.2f, cap: 1100, contrast: false);
    }

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForHotspot(SKBitmap source, ScreenRect box) =>
        CropScale(source, box, padRatio: 50, minPad: 8, targetMin: 1100, minScale: 2.0f, maxScale: 3.4f, cap: 2400, contrast: false);

    public static IReadOnlyList<ScreenRect> GameHotspots(int width, int height)
    {
        if (width < 80 || height < 80)
            return [];

        var left = new ScreenRect(0, 0, Math.Max(80, width * 62 / 100), height);
        var bottom = new ScreenRect(0, height * 52 / 100, width, Math.Max(80, height * 48 / 100));
        var top = new ScreenRect(0, 0, width, Math.Max(80, height * 28 / 100));
        return [left, bottom, top];
    }

    private static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) CropScale(
        SKBitmap source,
        ScreenRect box,
        int padRatio,
        int minPad,
        float targetMin,
        float minScale,
        float maxScale,
        float cap,
        bool contrast)
    {
        var pad = minPad <= 0 ? 0 : Math.Max(minPad, Math.Min(box.Width, box.Height) / Math.Max(1, padRatio));
        var x = Math.Clamp(box.X - pad, 0, Math.Max(0, source.Width - 1));
        var y = Math.Clamp(box.Y - pad, 0, Math.Max(0, source.Height - 1));
        var w = Math.Clamp(box.Width + pad * 2, 8, source.Width - x);
        var h = Math.Clamp(box.Height + pad * 2, 8, source.Height - y);
        var sourceBox = new ScreenRect(x, y, w, h);

        using var crop = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.Black);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, SKRect.Create(x, y, w, h), SKRect.Create(w, h), Nearest);
        }

        var minSide = Math.Min(w, h);
        var maxSide = Math.Max(w, h);
        var scale = Math.Clamp(targetMin / Math.Max(8, minSide), minScale, maxScale);
        if (maxSide * scale > cap)
            scale = cap / maxSide;
        var scaled = Scale(crop, scale, contrast, nearest: true);
        return (scaled.Bitmap, scaled.Scale, sourceBox);
    }

    private static void StretchContrast(SKBitmap bitmap, bool mild)
    {
        var n = bitmap.Width * bitmap.Height;
        if (n < 16)
            return;

        var hist = new int[256];
        var ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero)
            return;

        unsafe
        {
            var p = (byte*)ptr;
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                var l = (p[o + 2] * 30 + p[o + 1] * 59 + p[o] * 11) / 100;
                hist[l]++;
            }

            var cut = Math.Max(1, n / (mild ? 80 : 40));
            var acc = 0;
            var lo = 0;
            var hi = 255;
            for (var i = 0; i < 256; i++)
            {
                acc += hist[i];
                if (acc >= cut)
                {
                    lo = i;
                    break;
                }
            }

            acc = 0;
            for (var i = 255; i >= 0; i--)
            {
                acc += hist[i];
                if (acc >= cut)
                {
                    hi = i;
                    break;
                }
            }

            if (hi - lo < 40)
                return;

            var span = (float)(hi - lo);
            var strength = mild ? 0.55f : 1f;
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                for (var c = 0; c < 3; c++)
                {
                    var stretched = (p[o + c] - lo) * 255f / span;
                    var v = p[o + c] * (1 - strength) + stretched * strength;
                    p[o + c] = (byte)Math.Clamp(v, 0, 255);
                }
            }
        }
    }
}
