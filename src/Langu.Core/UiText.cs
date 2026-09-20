using System.Text.RegularExpressions;

namespace Langu.Core;

public static partial class UiText
{
    public static string NormalizeSource(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var t = text.Replace("\r\n", "\n").Trim();
        t = t.Replace(" ", "").Replace("\t", "");
        t = RepeatPhrase().Replace(t, "$1");
        return t;
    }

    public static string NormalizeTranslation(string translated, string source)
    {
        if (string.IsNullOrWhiteSpace(translated))
            return "";

        var t = translated.Trim();
        t = StripQuotes(t);
        t = LeadingJunk().Replace(t, "");
        t = RepeatWords().Replace(t, "$1");
        t = RepeatPhrase().Replace(t, "$1");
        t = Spaces().Replace(t, " ").Trim();
        t = StripQuotes(t);
        if (source.Trim().Length <= 12 && t.EndsWith('.') && t.Count(c => c == '.') == 1)
            t = t.TrimEnd('.');
        return t;
    }

    public static bool LooksGluedLatin(string text)
    {
        var t = text.Trim();
        if (t.Length < 16 || !LanguageDetector.IsMostlyLatin(t))
            return false;
        var letters = t.Count(char.IsLetter);
        var spaces = t.Count(char.IsWhiteSpace);
        return letters >= 16 && spaces == 0;
    }

    private static string StripQuotes(string text)
    {
        var t = text.Trim();
        const string marks = "「」『』\"'“”‚‘’";
        while (t.Length > 0 && marks.Contains(t[0]))
            t = t[1..].Trim();
        while (t.Length > 0 && marks.Contains(t[^1]))
            t = t[..^1].Trim();
        return t;
    }

    public static bool IsCompactUiLabel(string text)
    {
        var t = text.Trim();
        if (t.Length is < 2 or > 10)
            return false;
        if (t.Any(char.IsDigit) || t.Any(char.IsWhiteSpace))
            return false;
        if (!LanguageDetector.HasCjk(t))
            return false;
        return t.IndexOfAny(['。', '！', '？', '.', '!', '?', '、', ',']) < 0;
    }

    public static IReadOnlyList<string> SplitRepeated(string text)
    {
        var t = text.Replace("\r\n", "\n").Trim();
        if (t.Length < 4)
            return [t];

        for (var len = t.Length / 2; len >= 2; len--)
        {
            if (t.Length % len != 0)
                continue;
            var unit = t[..len];
            var copies = t.Length / len;
            if (copies < 2)
                continue;
            var ok = true;
            for (var i = 1; i < copies; i++)
            {
                if (!t.AsSpan(i * len, len).SequenceEqual(unit))
                {
                    ok = false;
                    break;
                }
            }

            if (ok && LanguageDetector.HasCjk(unit))
                return Enumerable.Repeat(unit, copies).ToList();
        }

        return [t];
    }

    [GeneratedRegex(@"^[\-–—•●]+\s*")]
    private static partial Regex LeadingJunk();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b(\p{L}+)(?:[\s,;:]+(?:e\s+)?\1)+\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatWords();

    [GeneratedRegex(@"(.{2,}?)\1{1,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatPhrase();
}
