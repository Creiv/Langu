using System.Diagnostics;
using Langu.Capture;
using Langu.Core;
using Langu.Ocr;
using Langu.Translation;

namespace Langu.App;

public sealed class TranslationPipeline : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly IOverlayController _overlay;
    private readonly ILanguageDetector _detector = new LanguageDetector();
    private readonly TranslationCache _cache = new();
    private readonly OcrEngineSelector _ocr = new();
    private readonly NllbTranslator _translator = new();
    private readonly ChangeGate _gate = new();
    private readonly PipelineStatus _status = new();
    private readonly object _stateLock = new();
    private readonly List<OverlayItem> _pins = [];
    private readonly List<OverlayItem> _probes = [];
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource _jobCts = new();
    private int _workEpoch;
    private IFrameCapture? _capture;
    private Task? _loop;
    private ScreenRect _bounds;
    private volatile bool _probeHeld;
    private int _busyCount;
    private bool _prunePinsNext;
    private bool _snapPins;
    private List<OverlayItem> _lastOcr = [];
    private OcrHudState _hud = OcrHudState.Hidden;
    private bool _focusSelecting;
    private bool _focusPending;
    private int _focusAnchorX;
    private int _focusAnchorY;
    private ScreenRect _focusPreview;
    private ScreenRect _focusCrop;

    public TranslationPipeline(AppSettings settings, IOverlayController overlay)
    {
        _settings = settings;
        _overlay = overlay;
    }

    public PipelineStatus Status => _status;
    public ITranslator Translator => _translator;
    public bool ProbeHeld => _probeHeld;
    public event Action? StatusChanged;

    public void ApplyLiveSettings() => _ocr.Configure(_settings.OcrEngine);

    public async Task PrepareModelsAsync(IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        _status.Message = "Download e caricamento modelli…";
        RaiseStatus();
        await OcrModelInstaller.EnsureAsync(progress, cancellationToken);
        await _translator.InitializeAsync(progress, cancellationToken);
        _status.ModelsReady = _translator.IsReady;
        _status.Message = _translator.IsReady ? "Modelli pronti" : _translator.UnavailableReason ?? "Modelli non pronti";
        RaiseStatus();
    }

    public async Task StartAsync()
    {
        await StopAsync();
        try
        {
            await OcrModelInstaller.EnsureAsync(null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _status.Warning = "OCR extra: " + ex.Message;
        }

        _ocr.Configure(_settings.OcrEngine);
        _gate.Reset();
        lock (_stateLock)
        {
            _pins.Clear();
            _probes.Clear();
        }

        _capture = await CompositeCapture.CreateAsync(_settings);
        _bounds = _capture.CurrentScreenBounds;
        _loopCts = new CancellationTokenSource();
        _status.Running = true;
        _status.Message = $"Pronto · tieni premuto {ProbeKeys.NameOf(_settings.ProbeKeyVk)}";
        _status.Warning = BuildWarning();
        RaiseStatus();
        _loop = Task.Run(() => LoopAsync(_loopCts.Token));
    }

    public async Task StopAsync()
    {
        _status.Running = false;
        _probeHeld = false;
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
            TryWake();
            _loopCts.Dispose();
            _loopCts = null;
        }

        if (_loop is not null)
        {
            try { await _loop; } catch { /* cancelled */ }
            _loop = null;
        }

        if (_capture is not null)
        {
            await _capture.DisposeAsync();
            _capture = null;
        }

        lock (_stateLock)
        {
            _pins.Clear();
            _probes.Clear();
        }

        ClearFocusState();
        _hud = OcrHudState.Hidden;
        _overlay.SetHud(_hud);
        _overlay.Update(Array.Empty<OverlayItem>(), ScreenRect.Empty);
        _status.Message = "In pausa";
        RaiseStatus();
    }

    public void SetProbeHeld(bool held)
    {
        if (!_status.Running)
            return;
        if (held)
        {
            if (!_probeHeld)
            {
                _prunePinsNext = true;
                _snapPins = true;
                _gate.Reset();
                ClearFocusState();
            }

            _probeHeld = true;
            if (_bounds.IsEmpty && _capture is not null)
                _bounds = _capture.CurrentScreenBounds;
            SetHud(OcrHudState.Busy("Riconoscimento…"));
            _status.Message = "Riconoscimento…";
            RaiseStatus();
        }
        else
        {
            _probeHeld = false;
            ClearFocusState();
            _hud = OcrHudState.Hidden;
            lock (_stateLock)
                _probes.RemoveAll(p => p.Kind == OverlayItemKind.Probe);
            Publish();
            var pending = CountBusy();
            _status.Message = pending > 0
                ? $"Traduzione in corso ({pending})…"
                : _pins.Count == 0
                    ? $"Pronto · tieni premuto {ProbeKeys.NameOf(_settings.ProbeKeyVk)}"
                    : $"{_pins.Count} traduzioni fissate · tieni premuto {ProbeKeys.NameOf(_settings.ProbeKeyVk)}";
            RaiseStatus();
        }

        TryWake();
    }

    public bool HasHit(int screenX, int screenY) => FindHit(screenX, screenY) is not null;

    public void BeginFocusPick(int screenX, int screenY)
    {
        if (!_status.Running || !_probeHeld)
            return;
        if (_bounds.IsEmpty && _capture is not null)
            _bounds = _capture.CurrentScreenBounds;
        _focusSelecting = true;
        _focusPending = false;
        _focusAnchorX = screenX;
        _focusAnchorY = screenY;
        _focusPreview = NormalizePick(screenX, screenY, screenX, screenY);
        SetHud(OcrHudState.Busy("Trascina un'area da rileggere"));
        _overlay.SetFocusBand(_focusPreview, selecting: true);
    }

    public void UpdateFocusPick(int screenX, int screenY)
    {
        if (!_focusSelecting || !_probeHeld)
            return;
        _focusPreview = NormalizePick(_focusAnchorX, _focusAnchorY, screenX, screenY);
        _overlay.SetFocusBand(_focusPreview, selecting: true);
    }

    public void EndFocusPick(int screenX, int screenY)
    {
        if (!_focusSelecting)
            return;
        _focusSelecting = false;
        if (!_probeHeld)
        {
            ClearFocusState();
            return;
        }

        var rect = NormalizePick(_focusAnchorX, _focusAnchorY, screenX, screenY);
        if (rect.Width < 24 || rect.Height < 24)
        {
            _focusPreview = ScreenRect.Empty;
            _overlay.SetFocusBand(_focusCrop, selecting: false);
            SetHud(_focusCrop.IsEmpty
                ? OcrHudState.Ready(ReadyHudText())
                : OcrHudState.Busy("Analisi area…"));
            return;
        }

        _focusCrop = rect;
        _focusPreview = rect;
        _focusPending = true;
        _gate.Reset();
        SetHud(OcrHudState.Busy("Analisi area selezionata…"));
        _overlay.SetFocusBand(_focusCrop, selecting: false);
        TryWake();
    }

    private bool HasCjkOverlay()
    {
        lock (_stateLock)
            return _probes.Concat(_pins).Any(item => LanguageDetector.HasReliableCjk(item.SourceText));
    }

    public void HandleClick(int screenX, int screenY, bool right)
    {
        if (!_status.Running || !_settings.OverlayEnabled)
            return;

        List<OverlayItem> targets;
        if (right)
        {
            lock (_stateLock)
                targets = _probes.Where(p => p.Kind == OverlayItemKind.Probe).ToList();
        }
        else
        {
            var hit = FindHit(screenX, screenY);
            targets = hit is null ? [] : [hit];
        }

        if (targets.Count == 0)
            return;

        foreach (var target in targets)
            MarkBusy(target);
        Publish();
        _ = Task.Run(() => TranslateManyAsync(targets));
    }

    private OverlayItem? FindHit(int screenX, int screenY)
    {
        lock (_stateLock)
        {
            return _probes.Concat(_pins)
                .Where(p => p.Kind != OverlayItemKind.Translated)
                .OrderBy(p => p.Shape.IsValid ? p.Shape.Length * p.Shape.Thickness : p.ScreenBounds.Area)
                .FirstOrDefault(p => p.Shape.IsTilted
                    ? p.Shape.Contains(screenX, screenY, 18)
                    : p.ScreenBounds.Inflate(10, 8).Contains(screenX, screenY));
        }
    }

    public void ClearPins()
    {
        lock (_stateLock)
        {
            _pins.Clear();
            if (!_probeHeld)
                _probes.Clear();
        }

        Publish();
    }

    public void AbortAll()
    {
        Interlocked.Increment(ref _workEpoch);
        try { _jobCts.Cancel(); } catch { /* ignore */ }
        _jobCts.Dispose();
        _jobCts = new();
        _probeHeld = false;
        _prunePinsNext = false;
        _snapPins = false;
        _lastOcr = [];
        ClearFocusState();
        lock (_stateLock)
        {
            _pins.Clear();
            _probes.Clear();
        }

        _hud = OcrHudState.Hidden;
        _overlay.SetHud(_hud);
        _overlay.SetFocusBand(ScreenRect.Empty, false);
        _overlay.Update(Array.Empty<OverlayItem>(), ScreenRect.Empty);
        _status.LastOcrCount = 0;
        _status.LastTranslatedCount = 0;
        _status.Message = $"Annullato · tieni premuto {ProbeKeys.NameOf(_settings.ProbeKeyVk)}";
        RaiseStatus();
        TryWake();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(1000.0 / Math.Max(1, _settings.FramesPerSecond), 250, 2000));
            try
            {
                if (_capture is null)
                    break;

                if (!_probeHeld)
                {
                    if (_hud.Visible)
                    {
                        _hud = OcrHudState.Hidden;
                        Publish();
                    }
                    else
                    {
                        Publish();
                    }

                    await WaitAsync(delay, cancellationToken);
                    continue;
                }

                if (_focusSelecting)
                {
                    await WaitAsync(delay, cancellationToken);
                    continue;
                }

                var frame = await _capture.CaptureAsync(cancellationToken);
                if (frame is null || frame.Width < 8 || frame.Height < 8)
                {
                    _status.Warning = BuildWarning() ?? "Nessun fotogramma.";
                    RaiseStatus();
                    await WaitAsync(delay, cancellationToken);
                    continue;
                }

                _bounds = frame.ScreenBounds;
                var focused = !_focusCrop.IsEmpty && (_focusPending || !_prunePinsNext);
                var ocrFrame = focused ? FrameBuffer.Crop(frame, _focusCrop) : frame;
                if (focused && (ocrFrame.Width < 8 || ocrFrame.Height < 8))
                {
                    _focusPending = false;
                    await WaitAsync(delay, cancellationToken);
                    continue;
                }

                if (!_focusPending && !_prunePinsNext && !_gate.HasChanged(ocrFrame, 8) && _probes.Count > 0)
                {
                    if (_probeHeld && !_hud.Working && !_focusSelecting)
                        SetHud(OcrHudState.Ready(ReadyHudText()));
                    await WaitAsync(delay, cancellationToken);
                    continue;
                }

                var searchAsian = focused
                    || !LanguageDetector.IsLatinHint(_settings.SourceLanguage)
                       && (_prunePinsNext || !HasCjkOverlay());
                var forceRapid = focused && _settings.OcrEngine != OcrEngineKind.Windows;
                var scannedFocus = focused;
                await ScanFrameAsync(
                    ocrFrame,
                    searchAsian,
                    forceRapid,
                    focused ? _focusCrop : ScreenRect.Empty,
                    cancellationToken);
                if (scannedFocus)
                    _focusPending = false;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.Warning = ex.Message;
                if (_probeHeld)
                    SetHud(OcrHudState.Ready("OCR interrotto — puoi rilasciare"));
                RaiseStatus();
            }

            try { await WaitAsync(delay, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ScanFrameAsync(
        CapturedFrame ocrFrame,
        bool searchAsian,
        bool forceRapid,
        ScreenRect replaceRegion,
        CancellationToken cancellationToken)
    {
        var epoch = _workEpoch;
        var rapid = _ocr.WillRunRapid(_settings.SourceLanguage, searchAsian, forceRapid);
        var area = replaceRegion.IsEmpty ? "" : "area · ";
        SetHud(OcrHudState.Busy(rapid
            ? $"Elaborazione · {area}Windows OCR"
            : $"Elaborazione · {area}OCR"));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _jobCts.Token);
        try
        {
            await _ocr.RecognizeStreamingAsync(
                ocrFrame,
                _settings.SourceLanguage,
                searchAsian,
                forceRapid,
                (lines, stage) =>
                {
                    if (_workEpoch != epoch)
                        return;
                    if (stage is "rapid-start")
                    {
                        if (_probeHeld)
                            SetHud(OcrHudState.Busy($"Elaborazione · {area}RapidOCR"));
                        return;
                    }

                    ApplyOcr(lines, ocrFrame, finalize: stage is not "windows" || !rapid, replaceRegion);
                    _status.LastOcrCount = lines.Count;
                    _status.LastTranslatedCount = _pins.Count;
                    _status.Warning = BuildWarning();
                    if (!_probeHeld)
                    {
                        RaiseStatus();
                        return;
                    }

                    if (stage is "windows" && rapid)
                    {
                        SetHud(OcrHudState.Busy($"Elaborazione · {area}RapidOCR · {CountProbes()} box"));
                        _status.Message = $"Box {CountProbes()} · RapidOCR in corso";
                    }
                    else
                    {
                        SetHud(OcrHudState.Ready(ReadyHudText()));
                        _status.Message = $"Box {CountProbes()} · fissate {_pins.Count} · click sx una / dx tutte";
                    }

                    RaiseStatus();
                },
                linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_workEpoch != epoch)
            return;
        if (_probeHeld && _hud.Working && !_focusSelecting)
            SetHud(OcrHudState.Ready(ReadyHudText()));
    }

    private void ApplyOcr(IReadOnlyList<OcrLine> lines, CapturedFrame frame, bool finalize, ScreenRect replaceRegion)
    {
        if (replaceRegion.IsEmpty)
            _bounds = frame.ScreenBounds;

        var incoming = new List<OverlayItem>();
        foreach (var line in lines.Where(l => l.Confidence >= _settings.MinOcrConfidence && !LooksLikeNoise(l)))
        {
            var screen = frame.MapToScreen(line.Bounds).Inflate(2, 2);
            var quad = frame.MapToScreen(line.Shape);
            var guess = _detector.DetectWithHint(line.Text, _settings.SourceLanguage);
            incoming.Add(new OverlayItem
            {
                Id = MakeId(line.Text, screen),
                SourceText = line.Text,
                SourceLanguage = guess.Iso639,
                ScreenBounds = quad.IsValid ? quad.Bounds.Inflate(2, 2) : screen,
                Quad = quad,
                Appearance = TextAppearance.FromFrame(frame, line.Bounds, line.Text),
                Kind = OverlayItemKind.Probe
            });
        }

        incoming = OcrBlockGrouper.Merge(incoming);

        var pruneAll = finalize && _prunePinsNext && replaceRegion.IsEmpty;
        if (finalize && replaceRegion.IsEmpty)
            _prunePinsNext = false;

        lock (_stateLock)
        {
            var next = replaceRegion.IsEmpty
                ? incoming
                : _probes.Where(p => !InRegion(p, replaceRegion)).Concat(incoming).ToList();

            if (replaceRegion.IsEmpty)
            {
                var stickyCjk = _probes.Where(p => LanguageDetector.HasReliableCjk(p.SourceText)).ToList();
                if (!next.Any(item => LanguageDetector.HasReliableCjk(item.SourceText)))
                {
                    foreach (var old in stickyCjk)
                    {
                        if (next.Any(item => SameBox(item, old)))
                            continue;
                        next.Add(old with { Kind = OverlayItemKind.Probe });
                    }
                }
            }

            _lastOcr = replaceRegion.IsEmpty
                ? next
                : _lastOcr.Where(item => !InRegion(item, replaceRegion)).Concat(incoming).ToList();
            var busy = _probes.Where(p => p.Kind == OverlayItemKind.Busy).ToList();
            var snapPins = _snapPins;
            RematchPins(
                replaceRegion.IsEmpty ? next : incoming,
                pruneAll || finalize && !replaceRegion.IsEmpty,
                replaceRegion,
                snapPins);
            if (snapPins && next.Count > 0)
                _snapPins = false;
            _probes.Clear();
            foreach (var item in next)
            {
                if (_pins.Any(p => SameBox(p, item)))
                    continue;
                var ongoing = busy.FirstOrDefault(p => SameBox(p, item));
                _probes.Add(ongoing ?? item);
            }

            if (!pruneAll)
            {
                foreach (var leftover in busy.Where(b => _probes.All(p => p.Id != b.Id)))
                    _probes.Add(leftover);
            }
        }

        Publish();
        if (_probeHeld && (!_focusCrop.IsEmpty || _focusSelecting))
            _overlay.SetFocusBand(_focusSelecting ? _focusPreview : _focusCrop, _focusSelecting);
    }

    private void RematchPins(List<OverlayItem> fresh, bool prune, ScreenRect onlyInside, bool snap)
    {
        var available = fresh.ToList();
        for (var i = _pins.Count - 1; i >= 0; i--)
        {
            var pin = _pins[i];
            var match = BestMatch(pin, available);
            if (match is not null)
            {
                available.Remove(match);
                _pins[i] = pin with
                {
                    ScreenBounds = BoxFollow.Follow(pin.ScreenBounds, match.ScreenBounds, snap),
                    Quad = snap || pin.ScreenBounds.IoU(match.ScreenBounds) < 0.2
                        ? match.Quad
                        : pin.Quad
                };
                continue;
            }

            if (prune && (onlyInside.IsEmpty || InRegion(pin, onlyInside)))
                _pins.RemoveAt(i);
        }
    }

    private static OverlayItem? BestMatch(OverlayItem pin, List<OverlayItem> fresh)
    {
        OverlayItem? best = null;
        var bestScore = 0d;
        foreach (var item in fresh)
        {
            var iou = item.ScreenBounds.IoU(pin.ScreenBounds);
            var same = SimilarSource(pin.SourceText, item.SourceText);
            if (!same && iou < 0.32)
                continue;
            var score = (same ? 1.4 : 0) + iou;
            if (score <= bestScore)
                continue;
            bestScore = score;
            best = item;
        }

        return best;
    }

    private static bool SimilarSource(string a, string b)
    {
        var left = a.Trim();
        var right = b.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;
        return left.Length >= 4 && right.Length >= 4 &&
               (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
                right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameBox(OverlayItem a, OverlayItem b) =>
        a.Id == b.Id
        || string.Equals(a.SourceText, b.SourceText, StringComparison.Ordinal) && a.ScreenBounds.IoU(b.ScreenBounds) > 0.25
        || a.ScreenBounds.IoU(b.ScreenBounds) > 0.4;

    private async Task TranslateManyAsync(IReadOnlyList<OverlayItem> targets)
    {
        if (targets.Count == 0)
            return;

        var epoch = _workEpoch;
        var job = _jobCts.Token;
        if (!_translator.IsReady)
        {
            _status.Warning = _translator.UnavailableReason ?? "Traduttore non pronto. Scarica i modelli.";
            foreach (var target in targets)
                Pin(target, target.SourceText, target.SourceLanguage);
            RaiseStatus();
            Publish();
            return;
        }

        Interlocked.Add(ref _busyCount, targets.Count);

        var targetIso = AppSettings.TargetIso(_settings.TargetLanguage);
        var targetNllb = AppSettings.TargetNllbCode(_settings.TargetLanguage);

        foreach (var target in targets)
        {
            try
            {
                if (_workEpoch != epoch || job.IsCancellationRequested)
                    return;

                var guess = _detector.DetectWithHint(target.SourceText, _settings.SourceLanguage);
                if (IsAlreadyTarget(guess, target.SourceText, targetIso))
                {
                    Pin(target, target.SourceText, guess.Iso639);
                    continue;
                }

                if (!_cache.TryGet(target.SourceText, _settings.TargetLanguage, out var translated) ||
                    string.IsNullOrWhiteSpace(translated) || SameUtterance(translated, target.SourceText))
                {
                    translated = await TranslateReliableAsync(
                        target.SourceText, guess, targetNllb, job);
                    if (_workEpoch != epoch)
                        return;
                    if (!string.IsNullOrWhiteSpace(translated) && !SameUtterance(translated, target.SourceText))
                        _cache.Set(target.SourceText, _settings.TargetLanguage, translated);
                }

                if (_workEpoch != epoch)
                    return;
                if (string.IsNullOrWhiteSpace(translated) || SameUtterance(translated, target.SourceText))
                {
                    _status.Warning = "Traduzione non riuscita. Riprova sulla box.";
                    if (_probeHeld)
                        SetHud(OcrHudState.Error("Traduzione non riuscita — clicca di nuovo"));
                    ReleaseBusy(target);
                    RaiseStatus();
                    continue;
                }

                Pin(target, translated, guess.Iso639);
                Publish();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (_workEpoch != epoch)
                    return;
                _status.Warning = ex.Message;
                if (_probeHeld)
                    SetHud(OcrHudState.Error("Traduzione interrotta — clicca di nuovo"));
                ReleaseBusy(target);
                RaiseStatus();
            }
            finally
            {
                Interlocked.Decrement(ref _busyCount);
            }
        }

        if (_workEpoch == epoch)
            Publish();
    }

    private bool IsAlreadyTarget(LanguageGuess guess, string text, string targetIso)
    {
        if (!string.Equals(guess.Iso639, targetIso, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(_settings.SourceLanguage)
            && !string.Equals(_settings.SourceLanguage, targetIso, StringComparison.OrdinalIgnoreCase))
            return false;
        if (LanguageDetector.HasCjk(text))
            return false;
        if (guess.Confidence < 0.8f)
            return false;
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 6;
    }

    private static bool SameUtterance(string a, string b)
    {
        static string Norm(string s)
        {
            var chars = s.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c));
            return string.Concat(chars).Replace("  ", " ").Trim();
        }

        var left = Norm(a);
        var right = Norm(b);
        if (left.Length < 2 || right.Length < 2)
            return false;
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        return left.Length >= 8 && right.Length >= 8
               && (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
               && Math.Abs(left.Length - right.Length) <= Math.Max(4, Math.Min(left.Length, right.Length) / 5);
    }

    private async Task<string> TranslateReliableAsync(
        string text, LanguageGuess guess, string targetNllb, CancellationToken job)
    {
        foreach (var source in SourceCandidates(guess, text))
        {
            if (string.Equals(source, targetNllb, StringComparison.OrdinalIgnoreCase))
                continue;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(job);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var translated = await _translator.TranslateAsync(text, source, targetNllb, timeout.Token);
            if (!string.IsNullOrWhiteSpace(translated) && !SameUtterance(translated, text))
                return translated;
        }

        return "";
    }

    private IEnumerable<string> SourceCandidates(LanguageGuess guess, string text)
    {
        var list = new List<string>();
        void Offer(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            if (list.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add(code);
        }

        if (!string.IsNullOrWhiteSpace(_settings.SourceLanguage))
            Offer(NllbLanguages.FromIso(_settings.SourceLanguage));
        Offer(guess.NllbCode);
        if (LanguageDetector.HasCjk(text))
        {
            Offer("jpn_Jpan");
            Offer("zho_Hans");
            Offer("kor_Hang");
        }

        if (LanguageDetector.IsMostlyLatin(text))
        {
            Offer("eng_Latn");
            Offer("ita_Latn");
            Offer("fra_Latn");
            Offer("deu_Latn");
            Offer("spa_Latn");
        }

        return list;
    }

    private void ReleaseBusy(OverlayItem target)
    {
        lock (_stateLock)
        {
            var index = _probes.FindIndex(p => p.Id == target.Id || p.ScreenBounds.IoU(target.ScreenBounds) > 0.4);
            if (index >= 0 && _probes[index].Kind == OverlayItemKind.Busy)
                _probes[index] = _probes[index] with { Kind = OverlayItemKind.Probe };
        }

        Publish();
    }

    private void MarkBusy(OverlayItem target)
    {
        var busy = target with { Kind = OverlayItemKind.Busy };
        lock (_stateLock)
        {
            var index = _probes.FindIndex(p => p.Id == target.Id || p.ScreenBounds.IoU(target.ScreenBounds) > 0.4);
            if (index >= 0)
                _probes[index] = busy;
            else
                _probes.Add(busy);
        }
    }

    private void Pin(OverlayItem target, string translated, string language)
    {
        lock (_stateLock)
        {
            var live = BestMatch(target, _lastOcr);
            var pinned = target with
            {
                TranslatedText = translated,
                SourceLanguage = language,
                Kind = OverlayItemKind.Translated,
                ScreenBounds = live is not null ? live.ScreenBounds : target.ScreenBounds,
                Quad = live is { Quad.IsValid: true } ? live.Quad : target.Quad
            };
            _probes.RemoveAll(p => p.Id == target.Id || p.ScreenBounds.IoU(target.ScreenBounds) > 0.4);
            _pins.RemoveAll(p => p.Id == target.Id || p.ScreenBounds.IoU(target.ScreenBounds) > 0.4);
            _pins.Add(pinned);
        }
    }

    private void SetHud(OcrHudState state)
    {
        _hud = state;
        Publish();
    }

    private string ReadyHudText()
    {
        var boxes = CountProbes();
        return boxes > 0
            ? $"Pronto · {boxes} box — puoi cliccare"
            : "Pronto — nessuna box, puoi rilasciare";
    }

    private void Publish()
    {
        var hud = _probeHeld ? _hud : OcrHudState.Hidden;
        if (!_settings.OverlayEnabled)
        {
            _overlay.SetHud(OcrHudState.Hidden);
            _overlay.SetFocusBand(ScreenRect.Empty, false);
            _overlay.Update(Array.Empty<OverlayItem>(), ScreenRect.Empty);
            return;
        }

        List<OverlayItem> shown;
        ScreenRect bounds;
        lock (_stateLock)
        {
            shown = _pins
                .Concat(_probes.Where(p => _probeHeld || p.Kind != OverlayItemKind.Probe))
                .ToList();
            bounds = _bounds;
        }

        if (bounds.IsEmpty && _capture is not null)
            bounds = _capture.CurrentScreenBounds;

        var focusVisible = _probeHeld && (_focusSelecting || !_focusCrop.IsEmpty);
        if (shown.Count == 0 && !hud.Visible && !focusVisible)
        {
            _overlay.SetHud(OcrHudState.Hidden);
            _overlay.SetFocusBand(ScreenRect.Empty, false);
            _overlay.Update(Array.Empty<OverlayItem>(), ScreenRect.Empty);
            return;
        }

        _overlay.Update(shown, bounds);
        _overlay.SetHud(hud);
        if (focusVisible)
            _overlay.SetFocusBand(_focusSelecting ? _focusPreview : _focusCrop, _focusSelecting);
        else
            _overlay.SetFocusBand(ScreenRect.Empty, false);
        _status.LastTranslatedCount = _pins.Count;
    }

    private void TryWake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var wake = _wake.WaitAsync(delay, cancellationToken);
        await wake;
    }

    private string? BuildWarning()
    {
        if (!_translator.IsReady)
            return _translator.UnavailableReason ?? "Scarica i modelli per tradurre offline.";

        if (_settings.CaptureMode == CaptureMode.Window)
        {
            var window = WindowEnumeration.TryGetWindow(_settings.TargetWindowHandle);
            if (window is null)
                return "Nessuna finestra selezionata.";
            if (window.IsMinimized)
                return "La finestra target è minimizzata.";
            if (window.LooksFullscreen)
                return "La finestra copre tutto lo schermo. Se è fullscreen esclusivo, passa a borderless.";
        }

        if (_settings.CaptureMode == CaptureMode.Region && _settings.RegionBounds.IsEmpty)
            return "Seleziona una regione dello schermo.";

        return null;
    }

    private static bool LooksLikeNoise(OcrLine line)
    {
        var trimmed = line.Text.Trim();
        return !OcrLineFilter.ShouldTranslate(trimmed) || OcrLineFilter.IsLikelyIcon(trimmed, line.Bounds);
    }

    private static string MakeId(string text, ScreenRect bounds) =>
        $"{text.Trim()}|{bounds.X / 8}:{bounds.Y / 8}:{bounds.Width / 8}:{bounds.Height / 8}";

    private int CountBusy()
    {
        lock (_stateLock)
            return _probes.Count(p => p.Kind == OverlayItemKind.Busy);
    }

    private int CountProbes()
    {
        lock (_stateLock)
            return _probes.Count(p => p.Kind == OverlayItemKind.Probe);
    }

    private void ClearFocusState()
    {
        _focusSelecting = false;
        _focusPending = false;
        _focusPreview = ScreenRect.Empty;
        _focusCrop = ScreenRect.Empty;
        _overlay.SetFocusBand(ScreenRect.Empty, false);
    }

    private ScreenRect NormalizePick(int x1, int y1, int x2, int y2)
    {
        var raw = new ScreenRect(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Abs(x2 - x1),
            Math.Abs(y2 - y1));
        return _bounds.IsEmpty ? raw : raw.Intersect(_bounds);
    }

    private static bool InRegion(OverlayItem item, ScreenRect region) =>
        !region.IsEmpty && !item.ScreenBounds.Intersect(region).IsEmpty;

    private void RaiseStatus() => StatusChanged?.Invoke();

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _translator.DisposeAsync();
        _ocr.Dispose();
        _wake.Dispose();
        _jobCts.Dispose();
    }
}
