using System.Text;
using System.Text.RegularExpressions;

namespace Langu.Core;

public static partial class OcrLineFilter
{
    public static bool IsUiChrome(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
            return true;
        if (Timestamp().IsMatch(t) || ViewCount().IsMatch(t) || Metric().IsMatch(t))
            return true;

        var lower = t.ToLowerInvariant();
        return lower is "4k" or "hdr" or "8k" or "hd" or "premium" or "subscribe" or "iscritto"
            or "views" or "visualizzazioni" or "translated to english" or "tradotto in inglese"
            or "mostra altro" or "show more" or "show less" or "rispondi" or "reply";
    }

    public static bool ShouldTranslate(string text) =>
        LanguageDetector.IsUsefulOcr(text) && !IsUiChrome(text) && !LanguageDetector.LooksLikeGarbage(text);

    public static bool IsLikelyIcon(string text, ScreenRect bounds)
    {
        var t = text.Trim();
        if (t.Length == 0)
            return true;

        if (LanguageDetector.LooksLikePrice(t) || LanguageDetector.HasReliableCjk(t) && t.Length >= 2)
            return bounds.Width < 10 && bounds.Height < 10;

        var letters = t.Count(char.IsLetter);
        var symbols = t.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
        var square = Math.Min(bounds.Width, bounds.Height) > 0
                     && Math.Abs(bounds.Width - bounds.Height) / (double)Math.Max(bounds.Width, bounds.Height) < 0.38;
        var tiny = bounds.Width < 16 && bounds.Height < 16 || bounds.Area < 90;
        var small = bounds.Height < 13 || bounds.Width < 13;

        if (tiny && t.Length <= 2)
            return true;
        if (square && small && t.Length <= 2 && !LanguageDetector.HasReliableCjk(t))
            return true;
        if (square && bounds.Height < 26 && t.Length == 1)
            return true;
        if (symbols > letters && t.Length <= 6)
            return true;
        if (IsMostlySymbols(t))
            return true;
        if (IsRepeatedJunk(t))
            return true;
        if (IsIconGibberish(t))
            return true;
        return false;
    }

    private static bool IsMostlySymbols(string text)
    {
        var useful = 0;
        var junk = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (Rune.IsLetterOrDigit(rune) || v is >= 0x3040 and <= 0x30FF or >= 0x3400 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF)
                useful++;
            else if (!Rune.IsWhiteSpace(rune))
                junk++;
        }

        return junk > 0 && junk >= useful;
    }

    private static bool IsRepeatedJunk(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length < 2)
            return false;
        return compact.Distinct().Count() == 1 && compact.Length <= 6;
    }

    private static bool IsIconGibberish(string text)
    {
        var t = text.Trim();
        if (t is "©" or "®" or "™" or "•" or "●" or "○" or "■" or "□" or "▶" or "►" or "★" or "☆" or "◆" or "→" or "←")
            return true;

        if (LanguageDetector.HasCjk(t))
        {
            var han = t.Count(c => c is >= '\u4E00' and <= '\u9FFF');
            var kana = t.Count(c => c is >= '\u3040' and <= '\u30FF');
            var latin = t.Count(char.IsAsciiLetter);
            return han <= 1 && kana == 0 && latin >= 1 && t.Length <= 4;
        }

        if (!LanguageDetector.IsMostlyLatin(t))
            return false;

        var letters = t.Where(char.IsLetter).ToArray();
        return letters.Length is >= 3 and <= 5 && !letters.Any(c => "aeiouàèéìòùAEIOU".Contains(c));
    }

    [GeneratedRegex(@"^(\d{1,2}:)?\d{1,2}:\d{2}$")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^\d+([.,]\d+)?\s*[kmb]$", RegexOptions.IgnoreCase)]
    private static partial Regex ViewCount();

    [GeneratedRegex(@"^\d+(\.\d+)?\s*(views|visualizzazioni|ago|fa)$", RegexOptions.IgnoreCase)]
    private static partial Regex Metric();
}
