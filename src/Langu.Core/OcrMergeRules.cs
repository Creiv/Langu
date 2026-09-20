namespace Langu.Core;

public static class OcrMergeRules
{
    public static OcrLine Prefer(OcrLine current, OcrLine extra)
    {
        var a = current.Text.Trim();
        var b = extra.Text.Trim();
        if (extra.Shape.IsTilted && !current.Shape.IsTilted)
            return extra;
        if (LanguageDetector.HasReliableCjk(b) && !LanguageDetector.HasReliableCjk(a))
            return extra;
        if (b.Length >= 8 && a.Contains(b, StringComparison.OrdinalIgnoreCase) && !extra.Shape.IsTilted)
            return current;
        if (a.Length >= 8 && b.Contains(a, StringComparison.OrdinalIgnoreCase) && b.Length > a.Length)
            return extra;
        if (b.Length > a.Length + 10 && LanguageDetector.IsUsefulOcr(b))
            return extra;
        return current;
    }
}
