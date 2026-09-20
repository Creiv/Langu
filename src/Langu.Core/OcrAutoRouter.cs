namespace Langu.Core;

public static class OcrAutoRouter
{
    public static bool NeedsRapid(IReadOnlyList<OcrLine> windows, string? languageHint, bool searchAsian)
    {
        var useful = windows.Where(Useful).ToList();
        if (useful.Count == 0)
            return true;
        if (LanguageDetector.IsCjkHint(languageHint))
            return true;
        if (useful.Any(line => LanguageDetector.HasReliableCjk(line.Text)))
            return useful.Count < 10;
        if (useful.Count >= 6 && useful.All(line => LanguageDetector.IsMostlyLatin(line.Text)))
            return false;
        return searchAsian || useful.Count < 4;
    }

    public static IReadOnlyList<OcrLine> Merge(IReadOnlyList<OcrLine> windows, IReadOnlyList<OcrLine> rapid)
    {
        var layout = windows.Where(Useful).Select(KeepTight).ToList();
        if (layout.Count == 0)
            return rapid.Where(Useful).Select(KeepTight).ToList();

        foreach (var extra in rapid.Where(Useful))
        {
            var hits = layout.Where(existing => SamePlace(existing, extra)).ToList();
            if (hits.Count == 0)
            {
                layout.Add(KeepTight(extra));
                continue;
            }

            if (hits.Count >= 2)
                continue;

            var current = hits[0];
            if (!ShouldAdoptText(current, extra))
                continue;
            var index = layout.IndexOf(current);
            layout[index] = new OcrLine
            {
                Text = extra.Text.Trim(),
                Bounds = current.Bounds,
                Quad = current.Quad,
                Confidence = Math.Max(current.Confidence, extra.Confidence),
                Origin = current.Origin
            };
        }

        return layout;
    }

    private static bool Useful(OcrLine line) =>
        LanguageDetector.HasCjk(line.Text) || LanguageDetector.IsUsefulOcr(line.Text);

    private static bool SamePlace(OcrLine a, OcrLine b) =>
        a.Bounds.Overlaps(b.Bounds, 0.35)
        || b.Bounds.Overlaps(a.Bounds, 0.35)
        || ContainsMostly(a.Bounds, b.Bounds)
        || ContainsMostly(b.Bounds, a.Bounds);

    private static bool ContainsMostly(ScreenRect outer, ScreenRect inner) =>
        !inner.IsEmpty && outer.Intersect(inner).Area >= inner.Area * 0.8;

    private static bool ShouldAdoptText(OcrLine current, OcrLine extra)
    {
        if (LanguageDetector.HasReliableCjk(extra.Text) && !LanguageDetector.HasReliableCjk(current.Text))
            return true;
        return LanguageDetector.LooksLikeGarbage(current.Text) && LanguageDetector.IsUsefulOcr(extra.Text);
    }

    private static OcrLine KeepTight(OcrLine line)
    {
        var bounds = line.Bounds;
        if (line.Quad.IsValid && line.Quad.Bounds.Height > bounds.Height + 2)
        {
            return new OcrLine
            {
                Text = line.Text,
                Bounds = bounds,
                Quad = TextQuad.FromRect(bounds),
                Confidence = line.Confidence,
                Origin = line.Origin
            };
        }

        return line;
    }
}
