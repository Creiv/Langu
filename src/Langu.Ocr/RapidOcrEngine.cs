using Langu.Core;
using RapidOcrNet;
using SkiaSharp;

namespace Langu.Ocr;

public sealed class RapidOcrEngine : IOcrEngine, IDisposable
{
    private static readonly RapidOcrOptions Options = RapidOcrOptions.Default with
    {
        Padding = 20,
        TextScore = 0.16f,
        BoxScoreThresh = 0.16f,
        BoxThresh = 0.12f,
        UnClipRatio = 1.75f,
        DoAngle = true
    };

    private readonly object _initLock = new();
    private RapidOcr? _latin;
    private RapidOcr? _cjk;
    private RapidOcr? _japan;
    private bool _initFailed;

    public string Name => _cjk is not null || _japan is not null ? "RapidOCR (CJK+latino)" : "RapidOCR";
    public bool IsAvailable
    {
        get
        {
            EnsureInit();
            return _latin is not null || _cjk is not null || _japan is not null;
        }
    }

    public Task<IReadOnlyList<OcrLine>> RecognizeAsync(
        CapturedFrame frame,
        string? languageHint,
        CancellationToken cancellationToken) =>
        RunAsync(frame, languageHint, light: false, cancellationToken);

    public Task<IReadOnlyList<OcrLine>> RecognizeCjkAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken) =>
        RunAsync(frame, "cjk", light: true, cancellationToken);

    private Task<IReadOnlyList<OcrLine>> RunAsync(
        CapturedFrame frame,
        string? languageHint,
        bool light,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInit();
        var engines = SelectEngines(languageHint);
        if (engines.Count == 0 || frame.Width < 8 || frame.Height < 8)
            return Task.FromResult<IReadOnlyList<OcrLine>>(Array.Empty<OcrLine>());

        using var original = BitmapConvert.ToSkia(frame);
        var enhanced = ImageEnhance.ForOcr(original);
        using var enhancedBitmap = enhanced.Bitmap;

        var lines = ReadAll(engines, enhancedBitmap, enhanced.Scale);
        if (lines.Count == 0)
            lines = ReadAll(engines, original, 1f);
        if (!light && lines.Count == 0)
        {
            using var inverted = ImageEnhance.Invert(enhancedBitmap);
            lines = ReadAll(engines, inverted, enhanced.Scale);
        }

        var paper = ReadPaper(engines, original);
        lines = Merge(lines, paper);
        if (paper.Count(l => LanguageDetector.HasCjk(l.Text)) < 6)
            lines = Merge(lines, ReadTiles(engines, original));
        if (!light)
            lines = RefineSmall(engines, original, lines);
        return Task.FromResult<IReadOnlyList<OcrLine>>(Dedup(lines));
    }

    private List<RapidOcr> SelectEngines(string? hint)
    {
        var hintIso = hint?.ToLowerInvariant();
        var list = new List<RapidOcr>();

        if (LanguageDetector.IsCjkHint(hintIso))
        {
            var asian = hintIso is "zh" or "zho_hans" or "ko" or "kor_hang"
                ? _cjk ?? _japan
                : _japan ?? _cjk;
            if (asian is not null)
                list.Add(asian);
            return list;
        }

        if (_latin is not null)
            list.Add(_latin);

        if (hintIso is "en" or "it" or "fr" or "de" or "es" or "pt" or "ru" or "ar")
            return list;

        var extra = _cjk ?? _japan;
        if (extra is not null && !list.Contains(extra))
            list.Add(extra);
        return list;
    }

    private static List<OcrLine> ReadAll(IReadOnlyList<RapidOcr> engines, SKBitmap bitmap, float imageScale)
    {
        if (engines.Count == 1)
            return Read(engines[0], bitmap, imageScale);

        var results = new List<OcrLine>[engines.Count];
        Parallel.For(0, engines.Count, i =>
        {
            using var copy = bitmap.Copy();
            results[i] = copy is null ? [] : Read(engines[i], copy, imageScale);
        });

        var merged = new List<OcrLine>();
        foreach (var set in results)
            merged = Merge(merged, set);
        return merged;
    }

    private static List<OcrLine> Read(RapidOcr engine, SKBitmap bitmap, float imageScale)
    {
        OcrResult result;
        try
        {
            result = engine.Detect(bitmap, Options);
        }
        catch
        {
            return [];
        }

        var lines = new List<OcrLine>();
        if (result.TextBlocks is null)
            return lines;

        foreach (var block in result.TextBlocks)
        {
            var text = (block.Text ?? string.Concat(block.Chars ?? [])).Trim();
            var quad = PointsToQuad(block.BoxPoints, imageScale);
            var bounds = quad.IsValid ? quad.Bounds : PointsToRect(block.BoxPoints, imageScale);
            if (bounds.IsEmpty || !KeepText(text, bounds))
                continue;

            var confidence = 0.75f;
            try
            {
                if (block.CharScores is { Length: > 0 })
                    confidence = (float)block.CharScores.Average();
            }
            catch
            {
                // keep default
            }

            if (LanguageDetector.LooksLikeGarbage(text))
                continue;

            lines.Add(new OcrLine
            {
                Text = text,
                Bounds = bounds,
                Quad = quad,
                Confidence = confidence
            });
        }

        return lines;
    }

    private static List<OcrLine> RefineSmall(IReadOnlyList<RapidOcr> engines, SKBitmap original, List<OcrLine> lines)
    {
        if (lines.Count == 0 || engines.Count == 0)
            return lines;

        var refined = new List<OcrLine>(lines.Count);
        var extras = new List<OcrLine>();
        var budget = 0;
        foreach (var line in lines)
        {
            if (budget >= 8 || (line.Bounds.Height >= 20 && line.Bounds.Width >= 40 && LanguageDetector.IsUsefulOcr(line.Text)))
            {
                refined.Add(line);
                continue;
            }

            budget++;
            var crop = ImageEnhance.ForSmallBox(original, line.Bounds);
            using var bitmap = crop.Bitmap;
            var found = ReadAll(engines, bitmap, 1f);
            if (found.Count == 0)
            {
                refined.Add(line);
                continue;
            }

            var best = found
                .OrderByDescending(l => LanguageDetector.HasCjk(l.Text) ? 2 : 0)
                .ThenByDescending(l => l.Confidence)
                .ThenByDescending(l => l.Text.Length)
                .First();
            var mapped = new ScreenRect(
                crop.SourceBox.X + (int)Math.Round(best.Bounds.X / crop.Scale),
                crop.SourceBox.Y + (int)Math.Round(best.Bounds.Y / crop.Scale),
                Math.Max(1, (int)Math.Round(best.Bounds.Width / crop.Scale)),
                Math.Max(1, (int)Math.Round(best.Bounds.Height / crop.Scale)));

            var keepBounds = mapped.Area >= line.Bounds.Area / 3 ? mapped : line.Bounds;
            refined.Add(new OcrLine
            {
                Text = PreferText(line.Text, best.Text),
                Bounds = keepBounds,
                Quad = line.Shape.IsValid ? line.Shape : TextQuad.FromRect(keepBounds),
                Confidence = Math.Max(line.Confidence, best.Confidence)
            });

            foreach (var extra in found.Skip(1))
            {
                extras.Add(new OcrLine
                {
                    Text = extra.Text,
                    Bounds = new ScreenRect(
                        crop.SourceBox.X + (int)Math.Round(extra.Bounds.X / crop.Scale),
                        crop.SourceBox.Y + (int)Math.Round(extra.Bounds.Y / crop.Scale),
                        Math.Max(1, (int)Math.Round(extra.Bounds.Width / crop.Scale)),
                        Math.Max(1, (int)Math.Round(extra.Bounds.Height / crop.Scale))),
                    Confidence = extra.Confidence
                });
            }
        }

        return Merge(refined, extras);
    }

    private static string PreferText(string original, string refined)
    {
        if (!LanguageDetector.IsUsefulOcr(original) && LanguageDetector.IsUsefulOcr(refined))
            return refined;
        if (LanguageDetector.HasReliableCjk(refined) && !LanguageDetector.HasReliableCjk(original))
            return refined;
        if (refined.Length > original.Length + 1 && !LanguageDetector.LooksLikeGarbage(refined))
            return refined;
        return LanguageDetector.LooksLikeGarbage(original) ? refined : original;
    }

    private static List<OcrLine> ReadPaper(IReadOnlyList<RapidOcr> engines, SKBitmap original)
    {
        var regions = PaperRegions.Find(original);
        if (regions.Count == 0 || engines.Count == 0)
            return [];

        var merged = new List<OcrLine>();
        foreach (var region in regions)
        {
            var crop = ImageEnhance.ForPaper(original, region);
            using var bitmap = crop.Bitmap;
            var found = MapCrop(ReadAll(engines, bitmap, crop.Scale), crop.SourceBox, crop.Scale);
            if (found.Count(l => LanguageDetector.HasCjk(l.Text)) < 4)
            {
                foreach (var angle in new[] { -18f, 18f })
                    found = Merge(found, ReadRotated(engines, bitmap, crop.SourceBox, crop.Scale, angle));
            }

            merged = Merge(merged, found);
        }

        return merged;
    }

    private static List<OcrLine> ReadRotated(
        IReadOnlyList<RapidOcr> engines,
        SKBitmap source,
        ScreenRect sourceBox,
        float imageScale,
        float degrees)
    {
        using var rotated = ImageEnhance.Rotate(source, degrees);
        var found = ReadAll(engines, rotated, 1f);
        var mapped = new List<OcrLine>();
        foreach (var line in found)
        {
            var quad = line.Shape.Map(p =>
            {
                var local = ImageEnhance.Unrotate(p, source.Width, source.Height, rotated.Width, rotated.Height, degrees);
                return new ScreenPoint(
                    sourceBox.X + (int)Math.Round(local.X / imageScale, MidpointRounding.AwayFromZero),
                    sourceBox.Y + (int)Math.Round(local.Y / imageScale, MidpointRounding.AwayFromZero));
            });
            mapped.Add(new OcrLine
            {
                Text = line.Text,
                Bounds = quad.IsValid ? quad.Bounds : line.Bounds.Offset(sourceBox.X, sourceBox.Y),
                Quad = quad,
                Confidence = line.Confidence
            });
        }

        return mapped;
    }

    private static List<OcrLine> MapCrop(IReadOnlyList<OcrLine> lines, ScreenRect sourceBox, float scale)
    {
        var mapped = new List<OcrLine>(lines.Count);
        foreach (var line in lines)
        {
            mapped.Add(new OcrLine
            {
                Text = line.Text,
                Bounds = line.Bounds.Offset(sourceBox.X, sourceBox.Y),
                Quad = line.Shape.Offset(sourceBox.X, sourceBox.Y),
                Confidence = line.Confidence
            });
        }

        _ = scale;
        return mapped;
    }

    private static List<OcrLine> ReadTiles(IReadOnlyList<RapidOcr> engines, SKBitmap original)
    {
        var tiles = BuildTiles(original.Width, original.Height);
        if (tiles.Count == 0 || engines.Count == 0)
            return [];

        var merged = new List<OcrLine>();
        foreach (var tile in tiles)
        {
            var crop = ImageEnhance.ForTile(original, tile);
            using var bitmap = crop.Bitmap;
            foreach (var line in ReadAll(engines, bitmap, crop.Scale))
            {
                merged.Add(new OcrLine
                {
                    Text = line.Text,
                    Bounds = line.Bounds.Offset(crop.SourceBox.X, crop.SourceBox.Y),
                    Quad = line.Shape.Offset(crop.SourceBox.X, crop.SourceBox.Y),
                    Confidence = line.Confidence
                });
            }
        }

        return merged;
    }

    private static List<ScreenRect> BuildTiles(int width, int height)
    {
        if (width < 1200 && height < 800)
            return [];

        var cols = width >= 1400 ? 2 : 1;
        var rows = height >= 800 ? 2 : 1;
        if (cols * rows <= 1)
            return [];

        var overlapX = width / 10;
        var overlapY = height / 10;
        var tileW = Math.Min(width, width / cols + overlapX);
        var tileH = Math.Min(height, height / rows + overlapY);
        var tiles = new List<ScreenRect>(cols * rows);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var x = cols == 1 ? 0 : col * (width - tileW) / (cols - 1);
                var y = rows == 1 ? 0 : row * (height - tileH) / (rows - 1);
                tiles.Add(new ScreenRect(x, y, tileW, tileH));
            }
        }

        return tiles;
    }

    private static bool KeepText(string text, ScreenRect bounds) =>
        OcrLineFilter.ShouldTranslate(text) && !OcrLineFilter.IsLikelyIcon(text, bounds);

    private static List<OcrLine> Merge(IReadOnlyList<OcrLine> primary, IReadOnlyList<OcrLine> extra)
    {
        var list = primary.ToList();
        foreach (var line in extra)
        {
            var overlap = list.FirstOrDefault(existing => existing.Bounds.Overlaps(line.Bounds, 0.4));
            if (overlap is null)
            {
                list.Add(line);
                continue;
            }

            var keepNew = ShouldReplace(overlap, line);
            if (keepNew)
            {
                list.Remove(overlap);
                list.Add(line);
            }
        }

        return list;
    }

    private static bool ShouldReplace(OcrLine current, OcrLine candidate)
    {
        var curCjk = LanguageDetector.HasReliableCjk(current.Text);
        var newCjk = LanguageDetector.HasReliableCjk(candidate.Text);
        var curLatin = LanguageDetector.IsMostlyLatin(current.Text) && LanguageDetector.IsUsefulOcr(current.Text);
        var newLatin = LanguageDetector.IsMostlyLatin(candidate.Text) && LanguageDetector.IsUsefulOcr(candidate.Text);

        if (newCjk && !curCjk && !curLatin)
            return true;
        if (curCjk && !newCjk)
            return false;
        if (curLatin && !newCjk)
            return false;
        if (newLatin && !curCjk && !curLatin)
            return true;
        if (LanguageDetector.LooksLikeGarbage(current.Text) && !LanguageDetector.LooksLikeGarbage(candidate.Text))
            return true;
        if (candidate.Confidence > current.Confidence + 0.12f && !curLatin)
            return true;
        return candidate.Text.Length > current.Text.Length + 4 && !curLatin;
    }

    private static List<OcrLine> Dedup(List<OcrLine> lines)
    {
        var ordered = lines.OrderByDescending(l => l.Confidence).ToList();
        var kept = new List<OcrLine>();
        foreach (var line in ordered)
        {
            if (kept.Any(existing =>
                    existing.Bounds.Overlaps(line.Bounds, 0.55) &&
                    string.Equals(existing.Text.Trim(), line.Text.Trim(), StringComparison.Ordinal)))
                continue;
            kept.Add(line);
        }

        return kept
            .OrderBy(l => l.Bounds.Y)
            .ThenBy(l => l.Bounds.X)
            .ToList();
    }

    private void EnsureInit()
    {
        if (_latin is not null || _cjk is not null || _japan is not null || _initFailed)
            return;

        lock (_initLock)
        {
            if (_latin is not null || _cjk is not null || _japan is not null || _initFailed)
                return;

            try
            {
                var bundled = Path.Combine(AppContext.BaseDirectory, "models", "v5");
                Directory.CreateDirectory(bundled);
                EnsureClassifier(bundled);

                var det = OcrModelInstaller.Find("ch_PP-OCRv5_mobile_det.onnx")
                          ?? Path.Combine(bundled, "ch_PP-OCRv5_mobile_det.onnx");
                var cls = OcrModelInstaller.Find("ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx")
                          ?? Path.Combine(bundled, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
                var latinRec = OcrModelInstaller.Find("latin_PP-OCRv5_rec_mobile_infer.onnx", "latin_PP-OCRv5_rec_mobile.onnx");
                var latinKeys = OcrModelInstaller.Find("ppocrv5_latin_dict.txt");
                var cjkRec = OcrModelInstaller.Find("ch_PP-OCRv5_rec_mobile.onnx", "ch_PP-OCRv5_rec_mobile_infer.onnx");
                var cjkKeys = OcrModelInstaller.Find("ppocrv5_dict.txt");
                var japanRec = OcrModelInstaller.Find("japan_PP-OCRv4_rec_mobile.onnx");
                var japanKeys = OcrModelInstaller.Find("japan_dict.txt");

                if (File.Exists(det) && File.Exists(cls) && latinRec is not null && latinKeys is not null)
                    _latin = Init(det, cls, latinRec, latinKeys);
                if (File.Exists(det) && File.Exists(cls) && cjkRec is not null && cjkKeys is not null)
                    _cjk = Init(det, cls, cjkRec, cjkKeys);
                if (File.Exists(det) && File.Exists(cls) && japanRec is not null && japanKeys is not null)
                    _japan = Init(det, cls, japanRec, japanKeys);

                if (_latin is null && _cjk is null && _japan is null)
                {
                    var fallback = new RapidOcr();
                    fallback.InitModels();
                    _latin = fallback;
                }
            }
            catch
            {
                _initFailed = true;
            }
        }
    }

    private static RapidOcr Init(string det, string cls, string rec, string keys)
    {
        var ocr = new RapidOcr();
        ocr.InitModels(det, cls, rec, keys);
        return ocr;
    }

    private static void EnsureClassifier(string models)
    {
        var cls = Path.Combine(models, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        if (File.Exists(cls) && new FileInfo(cls).Length > 1000)
            return;

        const string url = "https://raw.githubusercontent.com/BobLd/RapidOcrNet/master/RapidOcrNet/models/v5/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(cls, bytes);
        }
        catch
        {
            // Windows OCR resterà il fallback
        }
    }

    private static TextQuad PointsToQuad(SKPointI[]? points, float imageScale)
    {
        if (points is null || points.Length < 4)
            return TextQuad.FromRect(PointsToRect(points, imageScale));

        var scale = imageScale <= 0 ? 1f : imageScale;
        var mapped = new ScreenPoint[4];
        for (var i = 0; i < 4; i++)
        {
            mapped[i] = new ScreenPoint(
                (int)Math.Round(points[i].X / scale, MidpointRounding.AwayFromZero),
                (int)Math.Round(points[i].Y / scale, MidpointRounding.AwayFromZero));
        }

        return TextQuad.FromPoints(mapped);
    }

    private static ScreenRect PointsToRect(SKPointI[]? points, float imageScale)
    {
        if (points is null || points.Length == 0)
            return ScreenRect.Empty;

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var scale = imageScale <= 0 ? 1f : imageScale;
        return new ScreenRect(
            (int)Math.Round(minX / scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(minY / scale, MidpointRounding.AwayFromZero),
            Math.Max(1, (int)Math.Round((maxX - minX) / scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round((maxY - minY) / scale, MidpointRounding.AwayFromZero)));
    }

    public void Dispose()
    {
        _latin?.Dispose();
        _cjk?.Dispose();
        _japan?.Dispose();
    }
}
