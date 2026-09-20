namespace Langu.Core;

public static class OcrAutoRouter
{
    public static bool NeedsRapid(IReadOnlyList<OcrLine> windows, string? languageHint, bool searchAsian)
    {
        if (LanguageDetector.IsCjkHint(languageHint) || searchAsian)
            return true;
        return !windows.Any(Useful);
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
            {
                if (ShouldReplaceLatinCluster(hits, extra))
                {
                    foreach (var hit in hits)
                        layout.Remove(hit);
                    layout.Add(KeepTight(extra));
                }

                continue;
            }

            var current = hits[0];
            if (!ShouldAdoptText(current, extra))
                continue;
            var index = layout.IndexOf(current);
            layout[index] = Adopt(current, extra);
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

    private static bool ShouldReplaceLatinCluster(List<OcrLine> hits, OcrLine extra)
    {
        if (!LanguageDetector.HasReliableCjk(extra.Text))
            return false;
        if (hits.Any(hit => LanguageDetector.HasReliableCjk(hit.Text)))
            return false;
        var medianH = hits.Select(hit => hit.Bounds.Height).OrderBy(h => h).ElementAt(hits.Count / 2);
        if (extra.Bounds.Height > Math.Max(36, medianH * 2.4))
            return false;
        return hits.All(hit =>
            LanguageDetector.IsMostlyLatin(hit.Text) || LanguageDetector.LooksLikeGarbage(hit.Text));
    }

    private static bool ShouldAdoptText(OcrLine current, OcrLine extra)
    {
        if (LanguageDetector.HasReliableCjk(extra.Text) && !LanguageDetector.HasReliableCjk(current.Text))
            return true;
        return LanguageDetector.LooksLikeGarbage(current.Text) && LanguageDetector.IsUsefulOcr(extra.Text);
    }

    private static OcrLine Adopt(OcrLine current, OcrLine extra)
    {
        var takeRapidBox = LanguageDetector.HasReliableCjk(extra.Text)
                           && extra.Bounds.Width >= current.Bounds.Width
                           && extra.Bounds.Height <= Math.Max(current.Bounds.Height * 2.2, 36);
        var source = takeRapidBox ? KeepTight(extra) : current;
        return new OcrLine
        {
            Text = extra.Text.Trim(),
            Bounds = source.Bounds,
            Quad = TextQuad.FlattenLevel(source.Quad, source.Bounds),
            Confidence = Math.Max(current.Confidence, extra.Confidence),
            Origin = takeRapidBox ? extra.Origin : current.Origin
        };
    }

    private static OcrLine KeepTight(OcrLine line)
    {
        var bounds = line.Bounds;
        var quad = TextQuad.FlattenLevel(line.Quad, bounds);
        if (quad.IsValid && quad.Bounds.Height > bounds.Height + 2)
            quad = TextQuad.FromRect(bounds);
        return new OcrLine
        {
            Text = line.Text,
            Bounds = bounds,
            Quad = quad,
            Confidence = line.Confidence,
            Origin = line.Origin
        };
    }
}
