using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Langu.Capture;
using Langu.Core;
using Langu.Ocr;

namespace Langu.App;

internal static class SmokeTest
{
    public static int Run()
    {
        AppPaths.EnsureCreated();
        var log = new List<string>();
        try
        {
            var detector = new LanguageDetector();
            var it = detector.Detect("Questo è un testo italiano con delle parole comuni.");
            var en = detector.Detect("This is a simple English sentence with common words.");
            var ja = detector.Detect("日本語のテキストです");
            var mixedJa = detector.DetectWithHint("兵力が蓄えていた伊達軍", "en");
            var mixedEn = detector.DetectWithHint("Settings menu", "ja");
            Expect(log, it.Iso639 == "it", $"LID it={it.Iso639}");
            Expect(log, en.Iso639 == "en", $"LID en={en.Iso639}");
            Expect(log, ja.Iso639 == "ja", $"LID ja={ja.Iso639}");
            Expect(log, mixedJa.Iso639 == "ja", $"LID mixed ja={mixedJa.Iso639}");
            Expect(log, mixedEn.Iso639 == "en", $"LID mixed en={mixedEn.Iso639}");
            Expect(log, !LanguageDetector.HasReliableCjk("JAPAN 4K"), "no fake CJK");
            Expect(log, OcrLineFilter.IsUiChrome("16:05:39"), "filter timestamp");
            Expect(log, OcrLineFilter.ShouldTranslate("I really miss Japan"), "keep title");
            Expect(log, ProbeKeys.NameOf(ProbeKeys.DefaultVk).Contains("Alt", StringComparison.OrdinalIgnoreCase), "probe key");
            Expect(log, LanguageDetector.IsCjkHint("ja") && !LanguageDetector.IsCjkHint("en"), "cjk hint");
            Expect(log, LanguageDetector.LooksLikePrice("¥2,700") && LanguageDetector.IsUsefulOcr("募金"), "receipt text");
            Expect(log, OcrLineFilter.IsLikelyIcon("▶", new ScreenRect(10, 10, 18, 18)), "drop icon glyph");
            Expect(log, OcrLineFilter.IsLikelyIcon("®", new ScreenRect(40, 40, 14, 14)), "drop tiny mark");
            Expect(log, !OcrLineFilter.IsLikelyIcon("営業時間", new ScreenRect(80, 200, 220, 28)), "keep sign line");
            Expect(log, !OcrLineFilter.IsLikelyIcon("SAVE", new ScreenRect(20, 40, 52, 16)), "keep menu word");
            var grouped = OcrBlockGrouper.Merge([
                Dummy("営業時間 10:00", new ScreenRect(80, 200, 240, 26)),
                Dummy("定休日 水曜日", new ScreenRect(80, 230, 220, 26)),
                Dummy("ご来店ありがとう", new ScreenRect(80, 260, 250, 26))
            ]);
            Expect(log, grouped.Count == 3 && OcrReadingLayout.Group(grouped)[0].Kind == ReadingKind.Sentence,
                $"keep lines '{string.Join('|', grouped.Select(i => i.SourceText))}'");
            var tilted = TextQuad.FromPoints([
                new ScreenPoint(40, 20), new ScreenPoint(180, 60),
                new ScreenPoint(172, 88), new ScreenPoint(32, 48)
            ]);
            Expect(log, tilted.IsValid && tilted.IsTilted && tilted.Thickness < tilted.Length,
                $"quad angle={tilted.AngleDegrees:0.0} th={tilted.Thickness:0}");
            Expect(log, !TextQuad.FromRect(new ScreenRect(10, 20, 200, 24)).IsTilted, "straight quad");
            var split = SentenceSplitter.Split("Best-in-slot for her Cosmos DPS kit. If Shinku doesn't have it yet, Fluff of Ferocity is fine.");
            Expect(log, split.Count >= 2 && split.Any(p => p.Contains("doesn't have it yet", StringComparison.OrdinalIgnoreCase)), "split long");

            using var ocr = new RapidOcrEngine();
            Expect(log, ocr.IsAvailable, $"OCR available ({ocr.Name})");

            using var plain = RenderScene(plain: true);
            var plainLines = ocr.RecognizeAsync(ToFrame(plain), null, CancellationToken.None).GetAwaiter().GetResult();
            var plainText = string.Join(" | ", plainLines.Select(l => l.Text));
            Expect(log, ContainsAny(plainText, "Hello", "world", "Bonjour"), $"OCR plain '{plainText}'");

            using var colored = RenderScene(colored: true);
            var coloredLines = ocr.RecognizeAsync(ToFrame(colored), null, CancellationToken.None).GetAwaiter().GetResult();
            var coloredText = string.Join(" | ", coloredLines.Select(l => l.Text));
            Expect(log, ContainsAny(coloredText, "FOX", "JUMPS", "COLORED", "PIXEL"), $"OCR colored '{coloredText}'");

            using var pixel = RenderScene(pixelated: true);
            var pixelLines = ocr.RecognizeAsync(ToFrame(pixel), null, CancellationToken.None).GetAwaiter().GetResult();
            var pixelText = string.Join(" | ", pixelLines.Select(l => l.Text));
            Expect(log, ContainsAny(pixelText, "PIXEL", "GAME", "TEXT"), $"OCR pixel '{pixelText}'");

            using var tiny = RenderScene(tiny: true);
            var tinyLines = ocr.RecognizeAsync(ToFrame(tiny), null, CancellationToken.None).GetAwaiter().GetResult();
            var tinyText = string.Join(" | ", tinyLines.Select(l => l.Text));
            Expect(log, ContainsAny(tinyText, "SAVE", "LOAD", "QUEST"), $"OCR tiny '{tinyText}'");

            var mapped = new CapturedFrame
            {
                Bgra = new byte[200 * 100 * 4],
                Width = 200,
                Height = 100,
                Stride = 800,
                ScreenBounds = new ScreenRect(50, 80, 400, 200)
            }.MapToScreen(new ScreenRect(10, 10, 50, 20));
            Expect(log, mapped.X == 70 && mapped.Y == 100 && mapped.Width == 100 && mapped.Height == 40,
                $"map box {mapped.X},{mapped.Y} {mapped.Width}x{mapped.Height}");

            var aligned = new CapturedFrame
            {
                Bgra = new byte[200 * 100 * 4],
                Width = 200,
                Height = 100,
                Stride = 800,
                ScreenBounds = new ScreenRect(30, 40, 200, 100)
            }.MapToScreen(new ScreenRect(12, 8, 40, 10));
            Expect(log, aligned.X == 42 && aligned.Y == 48 && aligned.Width == 40 && aligned.Height == 10,
                $"map 1:1 {aligned.X},{aligned.Y} {aligned.Width}x{aligned.Height}");

            TryOcrSample(log, ocr);
            TryReleaseScenes(log, ocr);

            var cache = new TranslationCache();
            cache.Set("hola", TargetLanguage.Italian, "ciao");
            Expect(log, cache.TryGet("hola", TargetLanguage.Italian, out var cached) && cached == "ciao", "cache");

            var gdi = new GdiCapture(new ScreenRect(0, 0, 200, 120));
            var captured = gdi.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            Expect(log, captured is { Width: > 0 }, $"GDI capture {captured?.Width}x{captured?.Height}");

            File.WriteAllText(Path.Combine(AppPaths.Root, "smoke.txt"), string.Join(Environment.NewLine, log));
            return log.Any(l => l.StartsWith("FAIL")) ? 2 : 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppPaths.Root, "smoke.txt"), ex.ToString());
            return 1;
        }
    }

    private static void Expect(List<string> log, bool ok, string message) =>
        log.Add((ok ? "OK  " : "FAIL") + " " + message);

    private static bool ContainsAny(string text, params string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static OverlayItem Dummy(string text, ScreenRect bounds) => new()
    {
        Id = text,
        SourceText = text,
        SourceLanguage = "ja",
        ScreenBounds = bounds,
        Appearance = new TextAppearance
        {
            FillR = 245,
            FillG = 240,
            FillB = 230,
            TextR = 80,
            TextG = 40,
            TextB = 20
        }
    };

    public static int OcrFile(string path)
    {
        AppPaths.EnsureCreated();
        try
        {
            using var bitmap = new Bitmap(path);
            using var scaled = ScaleForOcr(bitmap);
            var frame = ToFrame(scaled);
            using var ocr = new OcrEngineSelector();
            var report = new List<string>();
            foreach (var kind in new[] { OcrEngineKind.Windows, OcrEngineKind.RapidOcr, OcrEngineKind.Auto })
            {
                ocr.Configure(kind);
                var lines = Task.Run(async () => await ocr.RecognizeAsync(frame, null, CancellationToken.None))
                    .GetAwaiter()
                    .GetResult();
                var heights = lines.Select(l => l.Bounds.Height).OrderBy(h => h).ToList();
                var median = heights.Count == 0 ? 0 : heights[heights.Count / 2];
                report.Add($"=== {kind} count={lines.Count} medianH={median} maxH={(heights.Count == 0 ? 0 : heights[^1])} ===");
                report.AddRange(lines.Select(l => $"{l.Bounds.Width}x{l.Bounds.Height} @ {l.Bounds.X},{l.Bounds.Y}  {l.Text}"));
                report.Add("");
            }

            var text = string.Join(Environment.NewLine, report);
            File.WriteAllText(Path.Combine(AppPaths.Root, "ocr-file.txt"), text);
            Console.WriteLine(text);
            return report.Any(l => l.StartsWith("=== Auto") && l.Contains("count=0")) ? 3 : 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppPaths.Root, "ocr-file.txt"), ex.ToString());
            return 1;
        }
    }

    private static void TryOcrSample(List<string> log, RapidOcrEngine ocr)
    {
        var sample = FindUp("tests", "pcsx2-sample.png");
        if (sample is null)
        {
            log.Add("SKIP pcsx2 sample missing");
            return;
        }

        if (!OcrModelInstaller.CjkReady)
        {
            log.Add("SKIP pcsx2 (CJK models missing — use Download models)");
            return;
        }

        using var bitmap = new Bitmap(sample);
        var lines = ocr.RecognizeAsync(ToFrame(bitmap), "ja", CancellationToken.None).GetAwaiter().GetResult();
        var text = string.Join(" | ", lines.Select(l => l.Text));
        Expect(log, ContainsAny(text, "天下", "伊達", "片倉", "竜", "兵", "報告"), $"OCR pcsx2 '{text}'");
    }

    private static void TryReleaseScenes(List<string> log, RapidOcrEngine ocr)
    {
        using var sign = RenderSign();
        var signLines = ocr.RecognizeAsync(ToFrame(sign), "ja", CancellationToken.None).GetAwaiter().GetResult();
        var signItems = ToItems(signLines);
        var grouped = OcrBlockGrouper.Merge(signItems);
        var joined = string.Join(" | ", grouped.Select(i => i.SourceText.Replace('\n', '/')));
        Expect(log,
            grouped.Any(i => i.SourceText.Contains("営業", StringComparison.Ordinal)
                             && (i.SourceText.Contains('\n') || grouped.Count >= 2)),
            $"OCR sign group '{joined}'");

        using var tilt = RenderTilt();
        var tiltLines = ocr.RecognizeAsync(ToFrame(tilt), "ja", CancellationToken.None).GetAwaiter().GetResult();
        var tiltText = string.Join(" | ", tiltLines.Select(l => l.Text));
        Expect(log, LanguageDetector.HasCjk(tiltText) || tiltLines.Any(l => l.Shape.IsTilted),
            $"OCR tilt '{tiltText}' tilted={tiltLines.Any(l => l.Shape.IsTilted)}");

        using var icons = RenderIcons();
        var iconLines = ocr.RecognizeAsync(ToFrame(icons), null, CancellationToken.None).GetAwaiter().GetResult();
        var kept = iconLines.Count(l => !OcrLineFilter.IsLikelyIcon(l.Text, l.Bounds));
        Expect(log, kept <= iconLines.Count, $"OCR icons kept={kept}/{iconLines.Count}");

        using var game = RenderBusyGame();
        var gameLines = ocr.RecognizeAsync(ToFrame(game), "ja", CancellationToken.None).GetAwaiter().GetResult();
        var gameText = string.Join(" | ", gameLines.Select(l => l.Text));
        Expect(log, LanguageDetector.HasCjk(gameText), $"OCR game ui '{gameText}'");
    }

    private static List<OverlayItem> ToItems(IReadOnlyList<OcrLine> lines) =>
        lines.Select(line => new OverlayItem
        {
            Id = line.Text,
            SourceText = line.Text,
            SourceLanguage = "ja",
            ScreenBounds = line.Bounds,
            Quad = line.Quad,
            Appearance = new TextAppearance
            {
                FillR = 245,
                FillG = 240,
                FillB = 230,
                TextR = 80,
                TextG = 40,
                TextB = 20
            }
        }).ToList();

    private static Bitmap RenderSign()
    {
        var bitmap = new Bitmap(640, 260, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.FromArgb(255, 243, 230, 201));
        using var font = CjkFont(28);
        g.DrawString("営業時間 10:00-20:00", font, new SolidBrush(System.Drawing.Color.FromArgb(255, 90, 45, 20)), 40, 40);
        g.DrawString("定休日 水曜日", font, new SolidBrush(System.Drawing.Color.FromArgb(255, 90, 45, 20)), 40, 90);
        g.DrawString("ご来店ありがとう", font, new SolidBrush(System.Drawing.Color.FromArgb(255, 90, 45, 20)), 40, 140);
        return bitmap;
    }

    private static Bitmap RenderTilt()
    {
        var bitmap = new Bitmap(640, 280, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.FromArgb(255, 243, 230, 201));
        g.TranslateTransform(80, 160);
        g.RotateTransform(-22);
        using var font = CjkFont(34);
        g.DrawString("斜めの看板", font, new SolidBrush(System.Drawing.Color.FromArgb(255, 90, 45, 20)), 0, 0);
        g.ResetTransform();
        return bitmap;
    }

    private static Bitmap RenderBusyGame()
    {
        var bitmap = new Bitmap(960, 540, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.FromArgb(255, 18, 92, 48));
        var rnd = new Random(7);
        for (var i = 0; i < 180; i++)
        {
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(
                255, rnd.Next(10, 70), rnd.Next(70, 160), rnd.Next(20, 90)));
            g.FillEllipse(brush, rnd.Next(-20, 940), rnd.Next(-20, 520), rnd.Next(30, 140), rnd.Next(20, 90));
        }

        using var font = CjkFont(18);
        using var outline = new SolidBrush(System.Drawing.Color.FromArgb(220, 10, 10, 10));
        using var ink = System.Drawing.Brushes.White;
        var lines = new[] { "天下統一に向けて兵力を蓄え", "着々と準備を進めていた伊達軍", "片倉小十郎", "竜の宝" };
        var y = 48;
        foreach (var line in lines)
        {
            g.DrawString(line, font, outline, 36, y + 1);
            g.DrawString(line, font, ink, 35, y);
            y += line.Length <= 6 ? 70 : 28;
        }

        return bitmap;
    }

    private static Bitmap RenderIcons()
    {
        var bitmap = new Bitmap(400, 120, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.FromArgb(255, 20, 20, 24));
        using var font = new Font("Segoe UI Symbol", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString("▶  ★  ®", font, System.Drawing.Brushes.White, 24, 32);
        return bitmap;
    }

    private static Font CjkFont(int size)
    {
        foreach (var name in new[] { "Yu Gothic UI", "Meiryo UI", "Microsoft YaHei UI", "Segoe UI" })
        {
            try
            {
                return new Font(name, size, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            catch
            {
                // try next
            }
        }

        return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static string? FindUp(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static Bitmap RenderScene(bool plain = false, bool colored = false, bool pixelated = false, bool tiny = false)
    {
        var bitmap = new Bitmap(900, 260, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.Clear(System.Drawing.Color.FromArgb(255, 18, 18, 18));

        if (plain)
        {
            g.Clear(System.Drawing.Color.White);
            using var font = new Font("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("Hello world Bonjour", font, System.Drawing.Brushes.Black, 24, 90);
            return bitmap;
        }

        if (colored)
        {
            g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(255, 192, 57, 43)), 20, 30, 860, 80);
            using var font = new Font("Segoe UI", 36, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("FOX JUMPS", font, System.Drawing.Brushes.White, 40, 48);
            g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(255, 108, 92, 231)), 20, 140, 860, 80);
            g.DrawString("COLORED PIXEL", font, System.Drawing.Brushes.White, 40, 158);
            return bitmap;
        }

        if (tiny)
        {
            g.Clear(System.Drawing.Color.FromArgb(255, 16, 48, 28));
            using var font = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString("SAVE  LOAD  QUEST", font, System.Drawing.Brushes.White, 18, 40);
            g.DrawString("HP 128", font, System.Drawing.Brushes.White, 18, 62);
            return bitmap;
        }

        using var chip = new Bitmap(120, 18, PixelFormat.Format32bppArgb);
        using (var tg = Graphics.FromImage(chip))
        {
            tg.Clear(System.Drawing.Color.FromArgb(255, 31, 58, 95));
            using var small = new Font("Consolas", 10, FontStyle.Bold, GraphicsUnit.Pixel);
            tg.DrawString("PIXEL GAME TEXT", small, System.Drawing.Brushes.White, 2, 2);
        }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(chip, new Rectangle(40, 80, 480, 72));
        return bitmap;
    }

    private static Bitmap ScaleForOcr(Bitmap source)
    {
        var max = Math.Max(source.Width, source.Height);
        var scale = max > 1600 ? 1600.0 / max : 1.0;
        var width = Math.Max(8, (int)Math.Round(source.Width * scale));
        var height = Math.Max(8, (int)Math.Round(source.Height * scale));
        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return dest;
    }

    private static CapturedFrame ToFrame(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var packed = new byte[bitmap.Width * bitmap.Height * 4];
            var destStride = bitmap.Width * 4;
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, packed, y * destStride, destStride);
            }

            return new CapturedFrame
            {
                Bgra = packed,
                Width = bitmap.Width,
                Height = bitmap.Height,
                Stride = destStride,
                ScreenBounds = new ScreenRect(0, 0, bitmap.Width, bitmap.Height)
            };
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
