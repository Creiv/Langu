using Langu.Core;
using SkiaSharp;

namespace Langu.Ocr;

internal static class PaperRegions
{
    public static List<ScreenRect> Find(SKBitmap source)
    {
        var cols = 22;
        var rows = 13;
        var cellW = source.Width / cols;
        var cellH = source.Height / rows;
        if (cellW < 10 || cellH < 10)
            return [];

        var bright = new bool[cols, rows];
        var ptr = source.GetPixels();
        if (ptr == IntPtr.Zero)
            return [];

        unsafe
        {
            var p = (byte*)ptr;
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var x = Math.Min(source.Width - 1, col * cellW + cellW / 2);
                    var y = Math.Min(source.Height - 1, row * cellH + cellH / 2);
                    var i = y * source.RowBytes + x * 4;
                    var l = (p[i + 2] * 30 + p[i + 1] * 59 + p[i] * 11) / 100;
                    bright[col, row] = l >= 168;
                }
            }
        }

        var seen = new bool[cols, rows];
        var regions = new List<ScreenRect>();
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                if (!bright[col, row] || seen[col, row])
                    continue;
                var box = Flood(bright, seen, col, row, cols, rows);
                var px = Math.Max(0, box.X * cellW - cellW / 4);
                var py = Math.Max(0, box.Y * cellH - cellH / 4);
                var pixel = new ScreenRect(
                    px,
                    py,
                    Math.Min(source.Width - px, box.Width * cellW + cellW / 2),
                    Math.Min(source.Height - py, box.Height * cellH + cellH / 2));
                var area = pixel.Area;
                if (area < 90 * 90 || area > source.Width * source.Height * 0.55)
                    continue;
                if (pixel.Y < source.Height * 0.07 && pixel.Height < source.Height * 0.18)
                    continue;
                regions.Add(pixel);
            }
        }

        return regions
            .OrderByDescending(r => r.Area)
            .Take(2)
            .ToList();
    }

    private static ScreenRect Flood(bool[,] bright, bool[,] seen, int startX, int startY, int cols, int rows)
    {
        var minX = startX;
        var minY = startY;
        var maxX = startX;
        var maxY = startY;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));
        seen[startX, startY] = true;
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            Try(x - 1, y);
            Try(x + 1, y);
            Try(x, y - 1);
            Try(x, y + 1);
        }

        return new ScreenRect(minX, minY, Math.Max(1, maxX - minX + 1), Math.Max(1, maxY - minY + 1));

        void Try(int x, int y)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows || seen[x, y] || !bright[x, y])
                return;
            seen[x, y] = true;
            stack.Push((x, y));
        }
    }
}
