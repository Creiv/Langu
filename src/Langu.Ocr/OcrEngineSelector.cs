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
        return force || _kind == OcrEngineKind.Auto;
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

        var windows = _windows.IsAvailable
            ? await _windows.RecognizeAsync(frame, languageHint, cancellationToken)
            : [];
        if (!_rapid.IsAvailable || !OcrAutoRouter.NeedsRapid(windows, languageHint, searchAsian))
            return windows;
        var rapid = await _rapid.RecognizeAsync(frame, languageHint, cancellationToken, light: windows.Count >= 3);
        return OcrAutoRouter.Merge(windows, rapid);
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

        if (_kind == OcrEngineKind.Windows || !_rapid.IsAvailable)
        {
            onPartial(
                _windows.IsAvailable
                    ? await _windows.RecognizeAsync(frame, languageHint, cancellationToken)
                    : [],
                "windows");
            return;
        }

        var windows = _windows.IsAvailable
            ? await _windows.RecognizeAsync(frame, languageHint, cancellationToken)
            : [];
        onPartial(windows, "windows");

        if (!forceRapid && !OcrAutoRouter.NeedsRapid(windows, languageHint, searchAsian))
        {
            onPartial(windows, "done");
            return;
        }

        onPartial([], "rapid-start");
        var rapid = await _rapid.RecognizeAsync(frame, languageHint, cancellationToken, light: windows.Count >= 3);
        onPartial(OcrAutoRouter.Merge(windows, rapid), "rapid");
    }

    private string SelectedLabel => _kind switch
    {
        OcrEngineKind.RapidOcr => _rapid.Name,
        OcrEngineKind.Windows => _windows.Name,
        _ => "Auto (Windows, Rapid for gaps)"
    };

    public void Dispose() => _rapid.Dispose();
}
