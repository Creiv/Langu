using System.Globalization;
using System.Text;

namespace Langu.Core;

/// <summary>
/// Identificatore lingua offline: script Unicode + profili n-gram/parole
/// (equivalente pratico a fastText lid.176 per le lingue più comuni a schermo).
/// </summary>
public sealed class LanguageDetector : ILanguageDetector
{
    private static readonly Dictionary<string, string[]> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ["the", "and", "you", "that", "was", "for", "are", "with", "this", "have", "not", "from"],
        ["it"] = ["che", "non", "per", "una", "sono", "con", "come", "della", "degli", "anche", "più", "questo"],
        ["fr"] = ["les", "des", "une", "est", "pas", "que", "pour", "dans", "qui", "sur", "avec", "plus"],
        ["de"] = ["der", "die", "und", "das", "ist", "nicht", "den", "von", "mit", "sich", "auf", "für"],
        ["es"] = ["que", "los", "las", "una", "del", "por", "con", "para", "como", "más", "este", "esta"],
        ["pt"] = ["que", "não", "uma", "para", "com", "por", "como", "mais", "dos", "das", "está", "são"],
        ["nl"] = ["het", "van", "een", "dat", "niet", "voor", "met", "zijn", "dit", "ook", "maar", "als"],
        ["pl"] = ["nie", "się", "jest", "na", "do", "to", "jak", "ale", "czy", "przez", "tylko", "już"],
        ["ro"] = ["este", "pentru", "nu", "sunt", "din", "care", "cu", "mai", "că", "sau", "pe", "la"],
        ["sv"] = ["och", "att", "det", "som", "för", "med", "inte", "den", "har", "på", "av", "ett"],
        ["da"] = ["og", "at", "det", "er", "til", "på", "af", "ikke", "den", "med", "for", "har"],
        ["cs"] = ["že", "se", "na", "je", "to", "jsou", "pro", "jak", "ale", "si", "od", "po"],
        ["hu"] = ["hogy", "nem", "egy", "van", "azt", "meg", "csak", "vagy", "kell", "ezt", "már"],
        ["tr"] = ["bir", "bu", "ve", "için", "değil", "olan", "daha", "ile", "gibi", "çok", "ama"],
        ["id"] = ["yang", "dan", "tidak", "untuk", "dengan", "ini", "dari", "pada", "adalah", "atau"],
        ["vi"] = ["không", "của", "là", "và", "một", "các", "trong", "được", "cho", "với", "này"]
    };

    public LanguageGuess Detect(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 1)
            return new LanguageGuess("und", "eng_Latn", 0);

        var script = DetectScript(trimmed);
        if (script is not null)
            return script.Value;

        var latin = DetectLatin(trimmed);
        return latin ?? new LanguageGuess("en", "eng_Latn", 0.2f);
    }

    public LanguageGuess DetectWithHint(string text, string? hint)
    {
        var detected = Detect(text);
        if (HasCjk(text) || IsNonLatinIso(detected.Iso639))
            return detected;
        if (IsMostlyLatin(text))
            return detected;
        if (!string.IsNullOrWhiteSpace(hint))
            return new LanguageGuess(hint, NllbLanguages.FromIso(hint), 0.65f);
        return detected;
    }

    public static bool IsCjkHint(string? hint)
    {
        var iso = hint?.Trim().ToLowerInvariant();
        return iso is "ja" or "jpn_jpan" or "zh" or "zho_hans" or "ko" or "kor_hang" or "cjk";
    }

    public static bool IsLatinHint(string? hint)
    {
        var iso = hint?.Trim().ToLowerInvariant();
        return iso is "en" or "it" or "fr" or "de" or "es" or "pt" or "nl" or "pl" or "ro"
            or "sv" or "da" or "cs" or "hu" or "tr" or "id" or "vi"
            or "eng_latn" or "ita_latn" or "fra_latn" or "deu_latn" or "spa_latn";
    }

    public static bool HasCjk(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (v is >= 0x3040 and <= 0x30FF or >= 0x3400 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF)
                return true;
        }
        return false;
    }

    public static bool HasReliableCjk(string text)
    {
        var han = 0;
        var kana = 0;
        var hang = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (v is >= 0x3040 and <= 0x30FF) kana++;
            else if (v is >= 0xAC00 and <= 0xD7AF) hang++;
            else if (v is >= 0x3400 and <= 0x9FFF) han++;
        }

        return kana >= 1 || hang >= 1 || han >= 2;
    }

    public static bool IsUsefulOcr(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (HasCjk(text) || HasReliableCjk(text) || LooksLikePrice(text))
            return true;
        var letters = text.Count(char.IsLetter);
        return letters >= 3 && !LooksLikeGarbage(text);
    }

    public static bool LooksLikePrice(string text)
    {
        foreach (var c in text)
        {
            if (c is '¥' or '￥' or '円' or '$' or '€')
                return true;
        }

        var digits = 0;
        var letters = 0;
        foreach (var c in text)
        {
            if (char.IsDigit(c))
                digits++;
            else if (char.IsLetter(c))
                letters++;
        }

        return digits >= 3 && digits >= letters;
    }

    private static bool IsNonLatinIso(string iso) =>
        iso is "ja" or "zh" or "ko" or "ar" or "ru" or "th" or "hi" or "he" or "el" or "uk" or "bg";

    public static bool IsMostlyLatin(string text)
    {
        var letters = 0;
        var latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
                continue;
            letters++;
            if (rune.Value < 0x250)
                latin++;
        }

        return letters > 0 && latin / (float)letters >= 0.8f;
    }

    public static bool LooksLikeGarbage(string text)
    {
        if (HasCjk(text) || HasReliableCjk(text) || LooksLikePrice(text))
            return false;
        var letters = text.Count(char.IsLetter);
        if (letters < 3)
            return text.Length >= 4;
        var vowels = text.Count(c => "aeiouàèéìòùAEIOU".Contains(c));
        return letters >= 5 && vowels == 0;
    }

    private static LanguageGuess? DetectScript(string text)
    {
        int han = 0, hira = 0, kata = 0, hang = 0, arab = 0, cyrl = 0, thai = 0, deva = 0, hebr = 0, grek = 0, letters = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.OtherLetter)
                letters++;
            else
                continue;

            var v = rune.Value;
            if (v is >= 0x3040 and <= 0x309F) hira++;
            else if (v is >= 0x30A0 and <= 0x30FF) kata++;
            else if (v is >= 0xAC00 and <= 0xD7AF) hang++;
            else if (v is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF) han++;
            else if (v is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F) arab++;
            else if (v is >= 0x0400 and <= 0x04FF) cyrl++;
            else if (v is >= 0x0E00 and <= 0x0E7F) thai++;
            else if (v is >= 0x0900 and <= 0x097F) deva++;
            else if (v is >= 0x0590 and <= 0x05FF) hebr++;
            else if (v is >= 0x0370 and <= 0x03FF) grek++;
        }

        if (letters == 0)
            return null;

        float Share(int n) => n / (float)letters;
        if (Share(hira + kata) > 0.08f || (hira + kata > 0 && han > 0))
            return new LanguageGuess("ja", "jpn_Jpan", 0.92f);
        if (Share(hang) > 0.15f)
            return new LanguageGuess("ko", "kor_Hang", 0.93f);
        if (Share(han) > 0.15f)
            return new LanguageGuess("zh", "zho_Hans", 0.88f);
        if (Share(arab) > 0.20f)
            return new LanguageGuess("ar", "arb_Arab", 0.88f);
        if (Share(cyrl) > 0.20f)
            return new LanguageGuess("ru", "rus_Cyrl", 0.75f);
        if (Share(thai) > 0.20f)
            return new LanguageGuess("th", "tha_Thai", 0.93f);
        if (Share(deva) > 0.20f)
            return new LanguageGuess("hi", "hin_Deva", 0.80f);
        if (Share(hebr) > 0.20f)
            return new LanguageGuess("he", "heb_Hebr", 0.90f);
        if (Share(grek) > 0.20f)
            return new LanguageGuess("el", "ell_Grek", 0.90f);
        return null;
    }

    private static LanguageGuess? DetectLatin(string text)
    {
        var tokens = text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '—', '-'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return null;

        string? best = null;
        var bestHits = 0;
        foreach (var (iso, words) in CommonWords)
        {
            var hits = tokens.Count(t => words.Contains(t));
            if (hits > bestHits)
            {
                bestHits = hits;
                best = iso;
            }
        }

        if (best is null || bestHits == 0)
            return GuessByDiacritics(text);

        var confidence = Math.Clamp(0.45f + bestHits / (float)Math.Max(tokens.Length, 1), 0.45f, 0.95f);
        return new LanguageGuess(best, NllbLanguages.FromIso(best), confidence);
    }

    private static LanguageGuess GuessByDiacritics(string text)
    {
        var it = CountAny(text, "àèéìòù");
        var fr = CountAny(text, "àâçéèêëîïôùûüœ");
        var de = CountAny(text, "äöüß");
        var es = CountAny(text, "ñ¿¡áéíóú");
        var pt = CountAny(text, "ãõçáâêóô");

        var max = new[] { (it, "it"), (fr, "fr"), (de, "de"), (es, "es"), (pt, "pt") }
            .OrderByDescending(x => x.Item1)
            .First();

        if (max.Item1 > 0)
            return new LanguageGuess(max.Item2, NllbLanguages.FromIso(max.Item2), 0.55f);

        return new LanguageGuess("en", "eng_Latn", 0.30f);
    }

    private static int CountAny(string text, string chars)
    {
        var n = 0;
        foreach (var c in text.ToLowerInvariant())
        {
            if (chars.Contains(c))
                n++;
        }
        return n;
    }
}
