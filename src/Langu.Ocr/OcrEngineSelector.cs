using Langu.Core;

namespace Langu.Ocr;

public sealed class OcrEngineSelector : IOcrEngine, IDisposable
{
    private readonly RapidOcrEngine _rapid = new();
    private readonly WindowsOcrEngine _windows = new();
    private OcrEngineKind _kind = OcrEngineKind.Auto;

    public string Name => SelectedLabel;
    public bool IsAvailable => _rapid.IsAvailable || _windows.IsAvailable;

    public void Configure(OcrEngineKind kind) => _kind = kind;

    public bool WillRunRapid(string? languageHint, bool searchAsian, bool force = false)
    {
        if (!_rapid.IsAvailable)
            return false;
        if (_kind == OcrEngineKind.RapidOcr)
            return true;
        if (_kind == OcrEngineKind.Windows)
            return false;
        return force || searchAsian && !LanguageDetector.IsLatinHint(languageHint);
    }

    public Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        CapturedFrame frame,
        string? languageHint,
        CancellationToken cancellationToken) =>
        RecognizeAsync(frame, languageHint, cancellationToken, searchAsian: true);

    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        CapturedFrame frame,
        string? languageHint,
        CancellationToken cancellationToken,
        bool searchAsian)
    {
        if (_kind == OcrEngineKind.Windows)
            return _windows.IsAvailable
                ? await _windows.RecognizeAsync(frame, languageHint, cancellationToken)
                : [];

        if (_kind == OcrEngineKind.RapidOcr)
            return _rapid.IsAvailable
                ? await _rapid.RecognizeAsync(frame, languageHint, cancellationToken)
                : [];

        var wantAsian = searchAsian
                        && _rapid.IsAvailable
                        && !LanguageDetector.IsLatinHint(languageHint);

        var windowsTask = _windows.IsAvailable
            ? _windows.RecognizeAsync(frame, languageHint, cancellationToken)
            : Task.FromResult<IReadOnlyList<OcrLine>>([]);

        if (!wantAsian)
            return await windowsTask;

        var rapidTask = Task.Run(() =>
        {
            var task = LanguageDetector.IsCjkHint(languageHint)
                ? _rapid.RecognizeAsync(frame, languageHint, cancellationToken)
                : _rapid.RecognizeCjkAsync(frame, cancellationToken);
            return task.GetAwaiter().GetResult();
        }, cancellationToken);

        await Task.WhenAll(windowsTask, rapidTask);
        return MergeSmart(await windowsTask, await rapidTask);
    }

    public async Task RecognizeStreamingAsync(
        CapturedFrame frame,
        string? languageHint,
        bool searchAsian,
        bool forceRapid,
        Action<IReadOnlyList<OcrLine>, string> onPartial,
        CancellationToken cancellationToken)
    {
        if (_kind == OcrEngineKind.RapidOcr)
        {
            onPartial([], "rapid-start");
            onPartial(
                _rapid.IsAvailable
                    ? await _rapid.RecognizeAsync(frame, languageHint, cancellationToken)
                    : [],
                "rapid");
            return;
        }

        if (_kind == OcrEngineKind.Windows || !WillRunRapid(languageHint, searchAsian, forceRapid))
        {
            onPartial(
                _windows.IsAvailable
                    ? await _windows.RecognizeAsync(frame, languageHint, cancellationToken)
                    : [],
                "windows");
            return;
        }

        var windowsTask = _windows.IsAvailable
            ? _windows.RecognizeAsync(frame, languageHint, cancellationToken)
            : Task.FromResult<IReadOnlyList<OcrLine>>([]);
        var rapidTask = Task.Run(() =>
        {
            var task = forceRapid || LanguageDetector.IsCjkHint(languageHint)
                ? _rapid.RecognizeAsync(frame, languageHint, cancellationToken)
                : _rapid.RecognizeCjkAsync(frame, cancellationToken);
            return task.GetAwaiter().GetResult();
        }, cancellationToken);

        var windows = await windowsTask;
        onPartial(windows, "windows");
        onPartial(MergeSmart(windows, await rapidTask), "rapid");
    }

    private string SelectedLabel => _kind switch
    {
        OcrEngineKind.RapidOcr => _rapid.Name,
        OcrEngineKind.Windows => _windows.Name,
        _ => "Auto (Windows + asiatico)"
    };

    private static IReadOnlyList<OcrLine> MergeSmart(IReadOnlyList<OcrLine> windows, IReadOnlyList<OcrLine> rapid)
    {
        var list = windows.ToList();
        foreach (var line in rapid)
        {
            if (!LanguageDetector.HasReliableCjk(line.Text) && !LanguageDetector.IsUsefulOcr(line.Text))
                continue;

            var overlap = list.FirstOrDefault(existing => existing.Bounds.Overlaps(line.Bounds, 0.4));
            if (overlap is null)
            {
                if (LanguageDetector.HasReliableCjk(line.Text) || LanguageDetector.IsUsefulOcr(line.Text))
                    list.Add(line);
                continue;
            }

            var keep = OcrMergeRules.Prefer(overlap, line);
            if (keep == overlap)
                continue;
            list.Remove(overlap);
            list.Add(keep);
        }

        return list;
    }

    public void Dispose() => _rapid.Dispose();
}
