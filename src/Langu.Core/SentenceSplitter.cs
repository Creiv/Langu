using System.Text.RegularExpressions;

namespace Langu.Core;

public static partial class SentenceSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        var trimmed = text.Trim();
        var parts = Sentence().Split(trimmed)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (parts.Count <= 1 && trimmed.Length < 120)
            return [trimmed];

        if (parts.Count <= 1)
            parts = Colon().Split(trimmed).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        var packed = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length <= 220)
            {
                packed.Add(part);
                continue;
            }

            packed.AddRange(Soft().Split(part).Select(p => p.Trim()).Where(p => p.Length > 0));
        }

        return packed.Count == 0 ? [trimmed] : packed;
    }

    [GeneratedRegex(@"(?<=[\.!?。！？])\s+")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"(?<=:)\s+")]
    private static partial Regex Colon();

    [GeneratedRegex(@"(?<=;)\s+|(?<=,)\s+(?=[A-Z""“])")]
    private static partial Regex Soft();
}
