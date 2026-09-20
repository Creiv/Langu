namespace Langu.Core;

public interface IFrameCapture : IAsyncDisposable
{
    ScreenRect CurrentScreenBounds { get; }
    string EngineName { get; }
    Task<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken);
}

public interface IOcrEngine
{
    bool IsAvailable { get; }
    string Name { get; }
    Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        CapturedFrame frame,
        string? languageHint,
        CancellationToken cancellationToken);
}

public interface ILanguageDetector
{
    LanguageGuess Detect(string text);
    LanguageGuess DetectWithHint(string text, string? hint);
}

public interface ITranslator : IAsyncDisposable
{
    bool IsReady { get; }
    string? UnavailableReason { get; }
    Task InitializeAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
    Task<string> TranslateAsync(string text, string sourceNllb, string targetNllb, CancellationToken cancellationToken);
}

public interface IOverlayController
{
    void ShowOverlay();
    void HideOverlay();
    void Update(IReadOnlyList<OverlayItem> items, ScreenRect overlayBounds);
    void SetHud(OcrHudState state);
    void SetFocusBand(ScreenRect rect, bool selecting);
}

public sealed class DownloadProgress
{
    public string Stage { get; init; } = "";
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp(BytesReceived / (double)TotalBytes.Value, 0, 1) : null;
}
