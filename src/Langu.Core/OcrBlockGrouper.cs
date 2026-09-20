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
                    if (!CanJoinWords(cluster, leftover[i]))
                        continue;
                    cluster.Add(leftover[i]);
                    leftover.RemoveAt(i);
                    grew = true;
                }
            }

            grouped.Add(cluster.Count == 1 ? cluster[0] : CombineLine(cluster));
        }

        return grouped;
    }

    private static bool CanJoinWords(List<OverlayItem> cluster, OverlayItem extra)
    {
        if (cluster.Any(c => c.Shape.IsTilted) || extra.Shape.IsTilted)
            return false;

        OverlayItem? best = null;
        var bestGap = double.MaxValue;
        foreach (var item in cluster)
        {
            if (!RowAligned(item.ScreenBounds, extra.ScreenBounds))
                continue;
            var gap = HorizontalGap(item.ScreenBounds, extra.ScreenBounds);
            if (gap >= bestGap)
                continue;
            bestGap = gap;
            best = item;
        }

        if (best is null)
            return false;

        var a = best.ScreenBounds;
        var b = extra.ScreenBounds;
        var h = Math.Max(1, (a.Height + b.Height) / 2.0);
        if (a.Height / (double)Math.Max(1, b.Height) is < 0.62 or > 1.6)
            return false;
        if (bestGap > h * 0.7)
            return false;
        if (OcrReadingLayout.LooksLikeMenuLabel(best.SourceText)
            && OcrReadingLayout.LooksLikeMenuLabel(extra.SourceText))
            return false;
        return true;
    }

    private static bool RowAligned(ScreenRect a, ScreenRect b)
    {
        var minH = Math.Max(1, Math.Min(a.Height, b.Height));
        var overlap = a.Intersect(new ScreenRect(a.X, b.Y, a.Width, b.Height)).Height / (double)minH;
        return overlap >= 0.5 || Math.Abs(a.CenterY - b.CenterY) <= Math.Max(4, minH * 0.28);
    }

    private static double HorizontalGap(ScreenRect a, ScreenRect b)
    {
        if (!a.Intersect(b).IsEmpty)
            return 0;
        return Math.Max(0, Math.Max(a.X, b.X) - Math.Min(a.X + a.Width, b.X + b.Width));
    }

    private static OverlayItem CombineLine(List<OverlayItem> cluster)
    {
        var ordered = cluster.OrderBy(i => i.ScreenBounds.X).ToList();
        var bounds = LineBounds(ordered);
        var cjk = ordered.Count(i => LanguageDetector.HasCjk(i.SourceText)) >= (ordered.Count + 1) / 2;
        var text = string.Join(cjk ? "" : " ", ordered.Select(i => i.SourceText.Trim()).Where(t => t.Length > 0));
        var look = TextAppearance.Blend(ordered.Select(i => i.Appearance).ToList());
        return new OverlayItem
        {
            Id = $"{text}|{bounds.X / 8}:{bounds.Y / 8}:{bounds.Width / 8}:{bounds.Height / 8}",
            SourceText = text,
            SourceLanguage = ordered[0].SourceLanguage,
            ScreenBounds = bounds,
            Quad = TextQuad.FromRect(bounds),
            Appearance = look,
            Kind = OverlayItemKind.Probe,
            Origin = ordered.All(i => i.Origin == OcrBoxOrigin.Windows)
                ? OcrBoxOrigin.Windows
                : OcrBoxOrigin.Rapid
        };
    }

    private static ScreenRect LineBounds(List<OverlayItem> ordered)
    {
        var left = ordered.Min(i => i.ScreenBounds.X);
        var right = ordered.Max(i => i.ScreenBounds.X + i.ScreenBounds.Width);
        var heights = ordered.Select(i => i.ScreenBounds.Height).OrderBy(h => h).ToArray();
        var medianH = Math.Max(1, heights[heights.Length / 2]);
        var centerY = (int)Math.Round(ordered.Average(i => i.ScreenBounds.CenterY));
        return new ScreenRect(left, centerY - medianH / 2, Math.Max(1, right - left), medianH);
    }
}
