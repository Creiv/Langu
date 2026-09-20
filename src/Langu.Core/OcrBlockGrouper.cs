namespace Langu.Core;

public static class OcrBlockGrouper
{
    public static List<OverlayItem> Merge(IReadOnlyList<OverlayItem> items)
    {
        if (items.Count <= 1)
            return items.ToList();

        var leftover = items
            .OrderBy(i => i.ScreenBounds.Y)
            .ThenBy(i => i.ScreenBounds.X)
            .ToList();
        var grouped = new List<OverlayItem>();

        while (leftover.Count > 0)
        {
            var cluster = new List<OverlayItem> { leftover[0] };
            leftover.RemoveAt(0);
            var grew = true;
            while (grew)
            {
                grew = false;
                for (var i = leftover.Count - 1; i >= 0; i--)
                {
                    if (cluster.Count >= 24)
                        break;
                    if (!CanJoin(cluster, leftover[i]))
                        continue;
                    cluster.Add(leftover[i]);
                    leftover.RemoveAt(i);
                    grew = true;
                }
            }

            grouped.Add(cluster.Count == 1 ? cluster[0] : Combine(cluster));
        }

        return grouped;
    }

    private static bool CanJoin(List<OverlayItem> cluster, OverlayItem extra)
    {
        if (cluster.Any(c => c.Shape.IsTilted) || extra.Shape.IsTilted)
            return CanJoinTilted(cluster, extra);

        OverlayItem? best = null;
        var bestGap = double.MaxValue;
        foreach (var item in cluster)
        {
            var gap = Gap(item.ScreenBounds, extra.ScreenBounds);
            if (gap >= bestGap)
                continue;
            bestGap = gap;
            best = item;
        }

        if (best is null)
            return false;

        var a = best.ScreenBounds;
        var b = extra.ScreenBounds;
        var cjk = LanguageDetector.HasCjk(best.SourceText) || LanguageDetector.HasCjk(extra.SourceText);
        var h = Math.Max(1, (a.Height + b.Height) / 2.0);
        var w = Math.Max(1, (a.Width + b.Width) / 2.0);
        var heightRatio = a.Height / (double)Math.Max(1, b.Height);
        var widthRatio = a.Width / (double)Math.Max(1, b.Width);
        if (heightRatio < 0.45 || heightRatio > 2.2)
            return false;

        var aligned = ColumnAligned(a, b) || RowAligned(a, b);
        if (!aligned)
            return false;

        var colorOk = cluster.All(c => c.Appearance.SimilarTo(extra.Appearance));
        var stacked = ColumnAligned(a, b) && bestGap <= h * (cjk ? 0.85 : 0.55);
        var inline = RowAligned(a, b) && bestGap <= w * (cjk ? 0.35 : 0.4);
        if (!stacked && !inline)
            return false;
        if (!colorOk && !(stacked && bestGap <= h * 0.35))
            return false;
        if (inline && (widthRatio < 0.35 || widthRatio > 2.8))
            return false;

        var union = cluster.Aggregate(ScreenRect.Empty, (x, y) => x.Union(y.ScreenBounds));
        var next = union.Union(b);
        if (next.Area > (union.Area + b.Area) * 2.8)
            return false;
        if (!cjk && !stacked && next.Height > h * 4)
            return false;
        return true;
    }

    private static bool CanJoinTilted(List<OverlayItem> cluster, OverlayItem extra)
    {
        if (!extra.Shape.IsTilted || cluster.Any(c => !c.Shape.IsTilted))
            return false;

        var angle = extra.Shape.AngleDegrees;
        if (cluster.Any(c => Math.Abs(AngleDelta(c.Shape.AngleDegrees, angle)) > 8))
            return false;
        if (!cluster.All(c => c.Appearance.SimilarTo(extra.Appearance)))
            return false;

        var bestGap = cluster.Min(c => Gap(c.ScreenBounds, extra.ScreenBounds));
        var h = Math.Max(1, extra.ScreenBounds.Height);
        return bestGap <= h * 0.7;
    }

    private static double AngleDelta(double a, double b)
    {
        var d = Math.Abs(a - b);
        return Math.Min(d, 180 - d);
    }

    private static bool ColumnAligned(ScreenRect a, ScreenRect b)
    {
        var minW = Math.Max(1, Math.Min(a.Width, b.Width));
        var left = Math.Abs(a.X - b.X) <= Math.Max(6, minW * 0.22);
        var right = Math.Abs(a.X + a.Width - (b.X + b.Width)) <= Math.Max(6, minW * 0.22);
        var center = Math.Abs(a.CenterX - b.CenterX) <= Math.Max(8, minW * 0.28);
        var overlap = a.Intersect(new ScreenRect(b.X, a.Y, b.Width, a.Height)).Width / (double)minW;
        return left || right || center || overlap >= 0.5;
    }

    private static bool RowAligned(ScreenRect a, ScreenRect b)
    {
        var minH = Math.Max(1, Math.Min(a.Height, b.Height));
        var overlap = a.Intersect(new ScreenRect(a.X, b.Y, a.Width, b.Height)).Height / (double)minH;
        return overlap >= 0.45 || Math.Abs(a.CenterY - b.CenterY) <= Math.Max(4, minH * 0.3);
    }

    private static double Gap(ScreenRect a, ScreenRect b)
    {
        if (!a.Intersect(b).IsEmpty)
            return 0;
        if (ColumnAligned(a, b))
            return Math.Max(0, Math.Max(a.Y, b.Y) - Math.Min(a.Y + a.Height, b.Y + b.Height));
        if (RowAligned(a, b))
            return Math.Max(0, Math.Max(a.X, b.X) - Math.Min(a.X + a.Width, b.X + b.Width));
        return Math.Sqrt(
            Math.Pow(a.CenterX - b.CenterX, 2) +
            Math.Pow(a.CenterY - b.CenterY, 2));
    }

    private static OverlayItem Combine(List<OverlayItem> cluster)
    {
        var bounds = cluster.Aggregate(ScreenRect.Empty, (a, b) => a.Union(b.ScreenBounds));
        var medianH = cluster.Select(i => i.ScreenBounds.Height).OrderBy(v => v).ElementAt(cluster.Count / 2);
        var stacked = bounds.Height >= medianH * 1.55;
        var banner = cluster.Count(i => i.ScreenBounds.Height > i.ScreenBounds.Width * 1.15) > cluster.Count / 2
                     && bounds.Width < medianH * 2.2;
        var vertical = banner;
        var ordered = cluster.OrderBy(i => i.ScreenBounds.Y).ThenBy(i => i.ScreenBounds.X);
        var cjk = cluster.Count(i => LanguageDetector.HasCjk(i.SourceText)) >= (cluster.Count + 1) / 2;
        var joiner = stacked || vertical ? "\n" : cjk ? "" : " ";
        var pieces = new List<string>();
        foreach (var item in ordered)
        {
            var piece = item.SourceText.Trim();
            if (piece.Length == 0 || pieces.Any(existing => SameLine(existing, piece)))
                continue;
            pieces.RemoveAll(existing => SameLine(piece, existing) && piece.Length > existing.Length);
            pieces.Add(piece);
        }

        var text = string.Join(joiner, pieces);
        var look = TextAppearance.Blend(cluster.Select(i => i.Appearance).ToList())
            .WithVertical(vertical);
        var guess = cluster
            .GroupBy(i => i.SourceLanguage)
            .OrderByDescending(g => g.Count())
            .First().Key;

        return new OverlayItem
        {
            Id = $"{text}|{bounds.X / 8}:{bounds.Y / 8}:{bounds.Width / 8}:{bounds.Height / 8}",
            SourceText = text,
            SourceLanguage = guess,
            ScreenBounds = bounds.Inflate(2, 2),
            Quad = CombinedQuad(cluster, bounds),
            Appearance = look,
            Kind = OverlayItemKind.Probe
        };
    }

    private static TextQuad CombinedQuad(List<OverlayItem> cluster, ScreenRect bounds)
    {
        if (cluster.Count == 0 || cluster.Any(i => !i.Shape.IsTilted))
            return TextQuad.FromRect(bounds);

        var angle = cluster.Average(i => i.Shape.AngleRadians);
        var cx = cluster.Average(i => (double)i.Shape.CenterX);
        var cy = cluster.Average(i => (double)i.Shape.CenterY);
        var length = cluster.Sum(i => i.Shape.Length) * 0.92;
        var thickness = cluster.Max(i => i.Shape.Thickness);
        var span = Math.Sqrt(
            Math.Pow(cluster.Max(i => i.Shape.CenterX) - cluster.Min(i => i.Shape.CenterX), 2) +
            Math.Pow(cluster.Max(i => i.Shape.CenterY) - cluster.Min(i => i.Shape.CenterY), 2));
        length = Math.Max(length, span + cluster.Average(i => i.Shape.Length) * 0.5);
        return TextQuad.FromCenter(cx, cy, length, thickness, angle);
    }

    private static bool SameLine(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        if (a.Length < 6 || b.Length < 6)
            return false;
        var longer = a.Length >= b.Length ? a : b;
        var shorter = a.Length >= b.Length ? b : a;
        return longer.Contains(shorter, StringComparison.Ordinal)
               && longer.Length - shorter.Length <= Math.Max(4, shorter.Length / 5);
    }
}
