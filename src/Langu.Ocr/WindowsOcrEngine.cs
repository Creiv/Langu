using Langu.Core;
using Windows.Globalization;
using Windows.Media.Ocr;
using OcrLine = Langu.Core.OcrLine;

namespace Langu.Ocr;

public sealed class WindowsOcrEngine : IOcrEngine
{
    public string Name => "Windows OCR";
    public bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        CapturedFrame frame,
        string? languageHint,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || frame.Width < 8 || frame.Height < 8)
            return Array.Empty<OcrLine>();

        var engines = CreateEngines(languageHint).ToList();
        if (engines.Count == 0)
            return Array.Empty<OcrLine>();

        using var bitmap = BitmapConvert.ToSoftwareBitmap(frame);
        List<OcrLine> merged = [];
        foreach (var engine in engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            merged = Merge(merged, ToLines(result));
            if (languageHint is null && merged.Any(l => LanguageDetector.HasReliableCjk(l.Text)))
                break;
        }

        return merged;
    }

    private static List<OcrLine> ToLines(Windows.Media.Ocr.OcrResult result)
    {
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            var words = new List<(string Text, ScreenRect Bounds)>();
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                var bounds = new ScreenRect(
                    (int)Math.Round(r.X),
                    (int)Math.Round(r.Y),
                    Math.Max(1, (int)Math.Round(r.Width)),
                    Math.Max(1, (int)Math.Round(r.Height)));
                words.Add(((word.Text ?? "").Trim(), bounds));
            }

            foreach (var row in ClusterRows(words))
            {
                var boxes = row.Select(w => w.Bounds).ToList();
                var union = boxes.Aggregate(ScreenRect.Empty, (a, b) => a.IsEmpty ? b : Union(a, b));
                if (union.IsEmpty)
                    continue;
                var text = string.Join(" ", row.Select(w => w.Text).Where(t => t.Length > 0)).Trim();
                if (!Keep(text, union))
                    continue;
                var quad = TextQuad.FromWordBoxes(boxes);
                lines.Add(new OcrLine
                {
                    Text = text,
                    Bounds = quad.IsValid ? quad.Bounds : union,
                    Quad = quad,
                    Confidence = 0.75f
                });
            }
        }

        return lines;
    }

    private static IEnumerable<OcrEngine> CreateEngines(string? languageHint)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in HintsToTry(languageHint))
        {
            OcrEngine? created = null;
            if (tag is not null)
            {
                try
                {
                    created = OcrEngine.TryCreateFromLanguage(new Language(MapHint(tag)));
                }
                catch
                {
                    created = null;
                }
            }
            else
            {
                created = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (created is null)
                continue;
            var key = created.RecognizerLanguage?.LanguageTag ?? tag ?? "user";
            if (!seen.Add(key))
                continue;
            yield return created;
        }
    }

    private static IEnumerable<string?> HintsToTry(string? hint)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            yield return hint;
            yield break;
        }

        yield return null;
        foreach (var tag in new[] { "ja", "zh", "ko" })
        {
            if (HasPack(tag))
                yield return tag;
        }
    }

    private static bool HasPack(string tag) =>
        OcrEngine.AvailableRecognizerLanguages.Any(language =>
            language.LanguageTag.StartsWith(tag, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<List<(string Text, ScreenRect Bounds)>> ClusterRows(
        List<(string Text, ScreenRect Bounds)> words)
    {
        if (words.Count <= 1)
        {
            yield return words;
            yield break;
        }

        var rows = new List<List<(string Text, ScreenRect Bounds)>>();
        foreach (var word in words.OrderBy(w => w.Bounds.CenterY).ThenBy(w => w.Bounds.X))
        {
            var row = rows.LastOrDefault(current =>
            {
                var y = current.Average(w => w.Bounds.CenterY);
                var h = current.Average(w => (double)w.Bounds.Height);
                return Math.Abs(y - word.Bounds.CenterY) <= Math.Max(7, h * 0.5);
            });
            if (row is null)
                rows.Add([word]);
            else
                row.Add(word);
        }

        foreach (var row in rows)
            yield return row.OrderBy(w => w.Bounds.X).ToList();
    }

    private static bool Keep(string text, ScreenRect bounds) =>
        OcrLineFilter.ShouldTranslate(text) && !OcrLineFilter.IsLikelyIcon(text, bounds);

    private static string MapHint(string hint) => hint.ToLowerInvariant() switch
    {
        "it" or "ita_latn" => "it-IT",
        "en" or "eng_latn" => "en-US",
        "fr" or "fra_latn" => "fr-FR",
        "de" or "deu_latn" => "de-DE",
        "es" or "spa_latn" => "es-ES",
        "ja" or "jpn_jpan" => "ja-JP",
        "zh" or "zho_hans" => "zh-CN",
        "ko" or "kor_hang" => "ko-KR",
        "ru" or "rus_cyrl" => "ru-RU",
        _ => hint
    };

    private static List<OcrLine> Merge(List<OcrLine> primary, IReadOnlyList<OcrLine> extra)
    {
        foreach (var line in extra)
        {
            if (primary.Any(existing => existing.Bounds.Overlaps(line.Bounds, 0.45) ||
                                        string.Equals(existing.Text, line.Text, StringComparison.OrdinalIgnoreCase)))
                continue;
            primary.Add(line);
        }

        return primary;
    }

    private static ScreenRect Union(ScreenRect a, ScreenRect b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }
}
