using Langu.Core;
using SkiaSharp;

namespace Langu.Ocr;

internal static class ImageEnhance
{
    public static (SKBitmap Bitmap, float Scale) ForOcr(SKBitmap source)
    {
        var minSide = Math.Min(source.Width, source.Height);
        var maxSide = Math.Max(source.Width, source.Height);
        float scale;
        if (maxSide >= 1600)
            scale = 1.12f;
        else if (minSide < 900)
            scale = 900f / minSide;
        else
            scale = 1.35f;
        if (maxSide * scale > 2200)
            scale = 2200f / maxSide;
        scale = Math.Clamp(scale, 1.05f, 2.2f);
        return Scale(source, scale, contrast: true);
    }

    public static (SKBitmap Bitmap, float Scale) Scale(SKBitmap source, float scale, bool contrast)
    {
        scale = Math.Max(1f, scale);
        var width = Math.Max(8, (int)Math.Round(source.Width * scale));
        var height = Math.Max(8, (int)Math.Round(source.Height * scale));
        var dest = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, SKRect.Create(width, height));
        }

        if (contrast)
            StretchContrast(dest);
        return (dest, scale);
    }

    public static SKBitmap Invert(SKBitmap source)
    {
        var dest = source.Copy() ?? throw new InvalidOperationException("Copia bitmap non riuscita.");
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

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForPaper(SKBitmap source, ScreenRect box)
    {
        var pad = Math.Max(12, Math.Min(box.Width, box.Height) / 18);
        var x = Math.Clamp(box.X - pad, 0, Math.Max(0, source.Width - 1));
        var y = Math.Clamp(box.Y - pad, 0, Math.Max(0, source.Height - 1));
        var w = Math.Clamp(box.Width + pad * 2, 8, source.Width - x);
        var h = Math.Clamp(box.Height + pad * 2, 8, source.Height - y);
        var sourceBox = new ScreenRect(x, y, w, h);

        using var crop = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, SKRect.Create(x, y, w, h), SKRect.Create(w, h));
        }

        var minSide = Math.Min(w, h);
        var maxSide = Math.Max(w, h);
        var scale = Math.Clamp(1500f / Math.Max(8, minSide), 2.1f, 4.2f);
        if (maxSide * scale > 2600)
            scale = 2600f / maxSide;
        var scaled = Scale(crop, scale, contrast: true);
        return (scaled.Bitmap, scaled.Scale, sourceBox);
    }

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

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForTile(SKBitmap source, ScreenRect box)
    {
        var x = Math.Clamp(box.X, 0, Math.Max(0, source.Width - 1));
        var y = Math.Clamp(box.Y, 0, Math.Max(0, source.Height - 1));
        var w = Math.Clamp(box.Width, 8, source.Width - x);
        var h = Math.Clamp(box.Height, 8, source.Height - y);
        var sourceBox = new ScreenRect(x, y, w, h);

        using var crop = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, SKRect.Create(x, y, w, h), SKRect.Create(w, h));
        }

        var minSide = Math.Min(w, h);
        var maxSide = Math.Max(w, h);
        var scale = Math.Clamp(1100f / Math.Max(8, minSide), 1.6f, 3.4f);
        if (maxSide * scale > 2000)
            scale = 2000f / maxSide;
        var scaled = Scale(crop, scale, contrast: true);
        return (scaled.Bitmap, scaled.Scale, sourceBox);
    }

    public static (SKBitmap Bitmap, float Scale, ScreenRect SourceBox) ForSmallBox(SKBitmap source, ScreenRect box)
    {
        var pad = Math.Max(6, Math.Max(box.Height / 3, 8));
        var x = Math.Clamp(box.X - pad, 0, Math.Max(0, source.Width - 1));
        var y = Math.Clamp(box.Y - pad, 0, Math.Max(0, source.Height - 1));
        var w = Math.Clamp(box.Width + pad * 2, 8, source.Width - x);
        var h = Math.Clamp(box.Height + pad * 2, 8, source.Height - y);
        var sourceBox = new ScreenRect(x, y, w, h);

        using var crop = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, SKRect.Create(x, y, w, h), SKRect.Create(w, h));
        }

        var target = 52f;
        var scale = Math.Clamp(target / Math.Max(8, h), 2.2f, 4.8f);
        if (w * scale > 900)
            scale = 900f / w;
        var scaled = Scale(crop, scale, contrast: true);
        return (scaled.Bitmap, scaled.Scale, sourceBox);
    }

    private static void StretchContrast(SKBitmap bitmap)
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

            var lowCut = Math.Max(1, n / 40);
            var highCut = Math.Max(1, n / 40);
            var acc = 0;
            var lo = 0;
            var hi = 255;
            for (var i = 0; i < 256; i++)
            {
                acc += hist[i];
                if (acc >= lowCut)
                {
                    lo = i;
                    break;
                }
            }
            acc = 0;
            for (var i = 255; i >= 0; i--)
            {
                acc += hist[i];
                if (acc >= highCut)
                {
                    hi = i;
                    break;
                }
            }
            if (hi - lo < 28)
                return;

            var span = (float)(hi - lo);
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                for (var c = 0; c < 3; c++)
                {
                    var v = (p[o + c] - lo) * 255f / span;
                    p[o + c] = (byte)Math.Clamp(v, 0, 255);
                }
            }
        }
    }
}
