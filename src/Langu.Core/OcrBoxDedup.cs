namespace Langu.Core;

public static class OcrBoxDedup
{
    public static List<OverlayItem> Merge(IReadOnlyList<OverlayItem> items)
    {
        if (items.Count <= 1)
            return items.ToList();

        var ordered = items
            .OrderByDescending(i => Score(i))
            .ThenByDescending(i => i.ScreenBounds.Area)
            .ToList();
        var kept = new List<OverlayItem>();
        foreach (var item in ordered)
        {
            var hit = kept.FindIndex(existing => SamePlace(existing, item));
            if (hit < 0)
            {
                kept.Add(item);
                continue;
            }

            kept[hit] = Prefer(kept[hit], item);
        }

        return kept
            .OrderBy(i => i.ScreenBounds.Y)
            .ThenBy(i => i.ScreenBounds.X)
            .ToList();
    }

    private static bool SamePlace(OverlayItem a, OverlayItem b)
    {
        var mixed = LanguageDetector.HasCjk(a.SourceText) != LanguageDetector.HasCjk(b.SourceText);
        if (mixed && !RowAligned(a.ScreenBounds, b.ScreenBounds))
            return false;
        if (mixed && a.ScreenBounds.IoU(b.ScreenBounds) < 0.62 && !ContainsMostly(a.ScreenBounds, b.ScreenBounds)
            && !ContainsMostly(b.ScreenBounds, a.ScreenBounds))
            return false;
        if (a.ScreenBounds.IoU(b.ScreenBounds) >= 0.28)
            return true;
        if (a.ScreenBounds.Overlaps(b.ScreenBounds, 0.55))
            return true;
        if (ContainsMostly(a.ScreenBounds, b.ScreenBounds) || ContainsMostly(b.ScreenBounds, a.ScreenBounds))
            return SameOrContainedText(a.SourceText, b.SourceText);
        return false;
    }

    private static bool ContainsMostly(ScreenRect outer, ScreenRect inner)
    {
        if (inner.IsEmpty || outer.IsEmpty)
            return false;
        return outer.Intersect(inner).Area >= inner.Area * 0.82;
    }

    private static bool SameOrContainedText(string a, string b)
    {
        var left = a.Trim();
        var right = b.Trim();
        if (left.Length == 0 || right.Length == 0)
            return true;
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;
        return left.Contains(right, StringComparison.OrdinalIgnoreCase)
               || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static OverlayItem Prefer(OverlayItem current, OverlayItem extra)
    {
        var keepText = extra.SourceText.Trim().Length > current.SourceText.Trim().Length + 1
                       && LanguageDetector.IsUsefulOcr(extra.SourceText)
            ? extra
            : current;
        if (LanguageDetector.HasReliableCjk(extra.SourceText) && !LanguageDetector.HasReliableCjk(current.SourceText))
            keepText = extra;
        else if (LanguageDetector.HasReliableCjk(current.SourceText) && !LanguageDetector.HasReliableCjk(extra.SourceText))
            keepText = current;

        var box = LanguageDetector.HasCjk(keepText.SourceText)
                  && RowAligned(current.ScreenBounds, extra.ScreenBounds)
            ? WiderLine(current.ScreenBounds, extra.ScreenBounds, keepText.ScreenBounds)
            : LanguageDetector.HasCjk(keepText.SourceText)
                ? keepText.ScreenBounds
                : Tighter(current.ScreenBounds, extra.ScreenBounds);
        return keepText with
        {
            ScreenBounds = box,
            Quad = TextQuad.FlattenLevel(keepText.Quad, box)
        };
    }

    private static ScreenRect WiderLine(ScreenRect a, ScreenRect b, ScreenRect fallback)
    {
        if (a.IsEmpty)
            return b;
        if (b.IsEmpty)
            return a;
        var left = Math.Min(a.X, b.X);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var height = Math.Min(a.Height, b.Height);
        var centerY = (a.CenterY + b.CenterY) / 2;
        if (height < 8)
            return fallback;
        return new ScreenRect(left, centerY - height / 2, Math.Max(1, right - left), height);
    }

    private static bool RowAligned(ScreenRect a, ScreenRect b)
    {
        var minH = Math.Max(1, Math.Min(a.Height, b.Height));
        return Math.Abs(a.CenterY - b.CenterY) <= Math.Max(6, minH * 0.55);
    }

    private static ScreenRect Tighter(ScreenRect a, ScreenRect b)
    {
        if (a.IsEmpty)
            return b;
        if (b.IsEmpty)
            return a;
        if (a.Height != b.Height)
            return a.Height < b.Height ? a : b;
        return a.Area <= b.Area ? a : b;
    }

    private static int Score(OverlayItem item)
    {
        var text = item.SourceText.Trim();
        var score = text.Length;
        if (LanguageDetector.HasCjk(text))
            score += 20;
        if (UiText.IsCompactUiLabel(text))
            score += 8;
        return score;
    }
}
