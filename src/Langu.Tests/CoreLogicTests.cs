using Langu.Core;
using Xunit;

namespace Langu.Tests;

public class CoreLogicTests
{
    [Fact]
    public void Grouper_keeps_all_stacked_cjk_lines()
    {
        var grouped = OcrBlockGrouper.Merge([
            Item("営業時間 10:00", new ScreenRect(80, 200, 240, 26)),
            Item("定休日 水曜日", new ScreenRect(80, 230, 220, 26)),
            Item("ご来店ありがとう", new ScreenRect(80, 260, 250, 26))
        ]);

        Assert.Single(grouped);
        Assert.Contains('\n', grouped[0].SourceText);
        Assert.Contains("定休日", grouped[0].SourceText, StringComparison.Ordinal);
        Assert.False(grouped[0].Shape.IsTilted);
    }

    [Fact]
    public void Grouper_does_not_merge_tilted_with_straight()
    {
        var tilted = TextQuad.FromPoints([
            new ScreenPoint(40, 20), new ScreenPoint(180, 60),
            new ScreenPoint(172, 88), new ScreenPoint(32, 48)
        ]);
        var items = new List<OverlayItem>
        {
            Item("Hello world", new ScreenRect(40, 20, 160, 28)),
            new()
            {
                Id = "tilt",
                SourceText = "斜めの文字",
                SourceLanguage = "ja",
                ScreenBounds = tilted.Bounds,
                Quad = tilted,
                Appearance = Look()
            }
        };

        var grouped = OcrBlockGrouper.Merge(items);
        Assert.Equal(2, grouped.Count);
        Assert.Contains(grouped, i => i.Shape.IsTilted);
    }

    [Fact]
    public void Icon_filter_keeps_real_words_drops_glyphs()
    {
        Assert.True(OcrLineFilter.IsLikelyIcon("▶", new ScreenRect(10, 10, 18, 18)));
        Assert.True(OcrLineFilter.IsLikelyIcon("®", new ScreenRect(40, 40, 14, 14)));
        Assert.False(OcrLineFilter.IsLikelyIcon("SAVE", new ScreenRect(20, 40, 52, 16)));
        Assert.False(OcrLineFilter.IsLikelyIcon("営業時間", new ScreenRect(80, 200, 220, 28)));
    }

    [Fact]
    public void Follow_adopts_live_size_on_snap()
    {
        var frozen = new ScreenRect(10, 10, 80, 20);
        var live = new ScreenRect(200, 180, 240, 90);
        var next = BoxFollow.Follow(frozen, live, adoptSize: true);
        Assert.Equal(live, next);
    }

    [Fact]
    public void Follow_keeps_size_when_live_is_much_larger()
    {
        var frozen = new ScreenRect(10, 10, 80, 20);
        var live = new ScreenRect(12, 12, 240, 90);
        var next = BoxFollow.Follow(frozen, live, adoptSize: false);
        Assert.Equal(80, next.Width);
        Assert.Equal(20, next.Height);
    }

    [Fact]
    public void Prefer_picks_tilted_and_cjk()
    {
        var straight = new OcrLine
        {
            Text = "JAPAN STORE",
            Bounds = new ScreenRect(10, 10, 120, 22),
            Quad = TextQuad.FromRect(new ScreenRect(10, 10, 120, 22)),
            Confidence = 0.8f
        };
        var tilted = new OcrLine
        {
            Text = "店舗",
            Bounds = new ScreenRect(12, 8, 110, 40),
            Quad = TextQuad.FromCenter(70, 28, 120, 22, 32 * Math.PI / 180),
            Confidence = 0.7f
        };

        Assert.True(tilted.Shape.IsTilted);
        Assert.Same(tilted, OcrMergeRules.Prefer(straight, tilted));

        var latin = new OcrLine { Text = "abc", Bounds = straight.Bounds, Confidence = 0.6f };
        var cjk = new OcrLine { Text = "こんにちは", Bounds = straight.Bounds, Confidence = 0.6f };
        Assert.Same(cjk, OcrMergeRules.Prefer(latin, cjk));
    }

    [Fact]
    public void Quad_tilt_range()
    {
        var flat = TextQuad.FromRect(new ScreenRect(10, 20, 200, 24));
        Assert.False(flat.IsTilted);

        var tilted = TextQuad.FromPoints([
            new ScreenPoint(40, 20), new ScreenPoint(180, 60),
            new ScreenPoint(172, 88), new ScreenPoint(32, 48)
        ]);
        Assert.True(tilted.IsValid);
        Assert.True(tilted.IsTilted);
    }

    [Fact]
    public void Splitter_keeps_both_sentences()
    {
        var split = SentenceSplitter.Split(
            "Best-in-slot for her Cosmos DPS kit. If Shinku doesn't have it yet, Fluff of Ferocity is fine.");
        Assert.True(split.Count >= 2);
        Assert.Contains(split, p => p.Contains("doesn't have it yet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detector_reads_common_languages()
    {
        var detector = new LanguageDetector();
        Assert.Equal("it", detector.Detect("Questo è un testo italiano con delle parole comuni.").Iso639);
        Assert.Equal("en", detector.Detect("This is a simple English sentence with common words.").Iso639);
        Assert.Equal("ja", detector.Detect("日本語のテキストです").Iso639);
        Assert.Equal("ja", detector.DetectWithHint("兵力が蓄えていた伊達軍", "en").Iso639);
        Assert.Equal("en", detector.DetectWithHint("Settings menu", "ja").Iso639);
    }

    private static OverlayItem Item(string text, ScreenRect bounds) => new()
    {
        Id = text,
        SourceText = text,
        SourceLanguage = "ja",
        ScreenBounds = bounds,
        Appearance = Look()
    };

    private static TextAppearance Look() => new()
    {
        FillR = 245,
        FillG = 240,
        FillB = 230,
        TextR = 80,
        TextG = 40,
        TextB = 20
    };
}
