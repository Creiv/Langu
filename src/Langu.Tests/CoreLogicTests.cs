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

        Assert.Equal(3, grouped.Count);
        var sense = OcrReadingLayout.Group(grouped);
        Assert.Single(sense);
        Assert.Equal(ReadingKind.Sentence, sense[0].Kind);
        Assert.Contains("定休日", OcrReadingLayout.JoinSources(sense[0].Members), StringComparison.Ordinal);
    }

    [Fact]
    public void Words_join_into_one_line()
    {
        var grouped = OcrBlockGrouper.Merge([
            Item("The", new ScreenRect(40, 80, 40, 22)),
            Item("cheapest", new ScreenRect(86, 80, 90, 22)),
            Item("way", new ScreenRect(182, 80, 40, 22))
        ]);
        Assert.Single(grouped);
        Assert.Equal("The cheapest way", grouped[0].SourceText);
    }

    [Fact]
    public void Grouper_keeps_centered_menu_items_apart()
    {
        var grouped = OcrBlockGrouper.Merge([
            Item("ストーリー", Centered(960, 120, 220, 36)),
            Item("外伝ストーリー", Centered(960, 180, 300, 36)),
            Item("天下統一", Centered(960, 240, 180, 36)),
            Item("自由合戦", Centered(960, 300, 180, 36)),
            Item("対戦", Centered(960, 360, 90, 36)),
            Item("大武闘会", Centered(960, 420, 180, 36)),
            Item("ギャラリー", Centered(960, 480, 200, 36)),
            Item("各種設定", Centered(960, 540, 160, 36))
        ]);

        Assert.Equal(8, grouped.Count);
        Assert.All(grouped, item => Assert.DoesNotContain('\n', item.SourceText));
        Assert.Contains(grouped, i => i.SourceText == "天下統一");
        Assert.Contains(grouped, i => i.SourceText == "対戦");
        Assert.All(OcrReadingLayout.Group(grouped), g => Assert.Equal(ReadingKind.Menu, g.Kind));
    }

    [Fact]
    public void Allocate_keeps_sentence_on_each_line()
    {
        var parts = OcrReadingLayout.Allocate(
            "Il cielo era coperto e la pioggia cadeva piano",
            ["空は曇っていた", "雨が静かに降っていた"]);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, part => Assert.False(string.IsNullOrWhiteSpace(part)));
    }

    [Fact]
    public void Glossary_translates_game_menu_titles()
    {
        Assert.True(GameUiGlossary.TryTranslate("ストーリー", "it", out var story));
        Assert.Equal("Storia", story);
        Assert.True(GameUiGlossary.TryTranslate("天下統一", "it", out var unify));
        Assert.Equal("Unificazione", unify);
        Assert.True(GameUiGlossary.TryTranslate("自由合戦", "it", out var free));
        Assert.Equal("Battaglia libera", free);
        Assert.True(GameUiGlossary.TryTranslate("戻る", "it", out var back));
        Assert.Equal("Indietro", back);
        Assert.Equal("Storia", UiText.NormalizeTranslation("Storia, storia, storia.", "ストーリー"));
    }

    [Fact]
    public void Menu_splitter_separates_joined_titles()
    {
        var items = MenuBlockSplitter.Split([
            Item("ストーリー外伝ストーリー", new ScreenRect(800, 100, 200, 120))
        ]);
        Assert.Equal(2, items.Count);
        Assert.Equal("ストーリー", items[0].SourceText);
        Assert.Equal("外伝ストーリー", items[1].SourceText);
        Assert.True(items[0].ScreenBounds.Y < items[1].ScreenBounds.Y);
    }

    [Fact]
    public void Menu_splitter_keeps_single_line_box()
    {
        var items = MenuBlockSplitter.Split([
            Item("ストーリー外伝ストーリー", new ScreenRect(800, 100, 320, 34))
        ]);
        Assert.Single(items);
        Assert.Equal("ストーリー外伝ストーリー", items[0].SourceText);
    }

    [Fact]
    public void Dedup_merges_overlapping_copies()
    {
        var merged = OcrBoxDedup.Merge([
            Item("ストーリー", new ScreenRect(800, 100, 200, 36)),
            Item("ストーリー", new ScreenRect(808, 104, 190, 34)),
            Item("Bond gifts", new ScreenRect(400, 80, 220, 24))
        ]);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, i => i.SourceText == "ストーリー");
        Assert.Contains(merged, i => i.SourceText == "Bond gifts");
        Assert.True(merged.Single(i => i.SourceText == "ストーリー").ScreenBounds.Height <= 36);
    }

    [Fact]
    public void Router_keeps_windows_menu_when_rapid_is_one_tall_box()
    {
        var windows = Enumerable.Range(0, 8)
            .Select(i => Line($"Voce{i}xx", new ScreenRect(400, 80 + i * 40, 180, 26)))
            .ToList();
        var rapid = new[]
        {
            Line("ストーリー全部まとめて", new ScreenRect(380, 70, 240, 340))
        };

        var merged = OcrAutoRouter.Merge(windows, rapid);
        Assert.Equal(8, merged.Count);
        Assert.All(merged, line => Assert.True(line.Bounds.Height <= 28));
        Assert.Contains(merged, line => line.Text == "Voce0xx");
    }

    [Fact]
    public void Router_adds_cjk_in_empty_gap()
    {
        var windows = new[] { Line("Hello", new ScreenRect(40, 40, 80, 20)) };
        var rapid = new[] { Line("日本語です", new ScreenRect(40, 200, 140, 24)) };
        var merged = OcrAutoRouter.Merge(windows, rapid);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, line => line.Text == "日本語です");
        Assert.Equal(24, merged.Single(line => line.Text == "日本語です").Bounds.Height);
    }

    [Fact]
    public void Router_keeps_windows_height_when_adopting_cjk_text()
    {
        var windows = new[] { Line("abcde", new ScreenRect(80, 100, 160, 28)) };
        var rapid = new[] { Line("ストーリー", new ScreenRect(70, 90, 220, 90)) };
        var merged = OcrAutoRouter.Merge(windows, rapid);
        Assert.Single(merged);
        Assert.Equal("ストーリー", merged[0].Text);
        Assert.Equal(28, merged[0].Bounds.Height);
    }

    [Fact]
    public void Router_skips_rapid_on_dense_latin()
    {
        var windows = Enumerable.Range(0, 8)
            .Select(i => Line($"Title{i}", new ScreenRect(20, i * 30, 120, 18)))
            .ToList();
        Assert.False(OcrAutoRouter.NeedsRapid(windows, null, true));
        Assert.True(OcrAutoRouter.NeedsRapid([], null, true));
    }

    [Fact]
    public void UiText_strips_quotes_and_flags_glued_latin()
    {
        Assert.Equal("lottare", UiText.NormalizeTranslation("lottare\"", "戦う"));
        Assert.Equal("primavera", UiText.NormalizeTranslation("「primavera」", "春"));
        Assert.True(UiText.LooksGluedLatin("Finaledelpiuforforte"));
        Assert.False(UiText.LooksGluedLatin("Sconfiggi l'esercito"));
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
        Assert.Equal(frozen, next);
    }

    [Fact]
    public void Follow_ignores_small_ocr_jitter()
    {
        var frozen = new ScreenRect(100, 140, 180, 24);
        var live = new ScreenRect(103, 142, 188, 27);
        Assert.Equal(frozen, BoxFollow.Follow(frozen, live, adoptSize: false));
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
    public void Focus_crop_keeps_only_intersecting_boxes()
    {
        var crop = new ScreenRect(100, 100, 200, 80);
        var picked = new[]
        {
            Item("dentro", new ScreenRect(120, 110, 80, 20)),
            Item("fuori", new ScreenRect(400, 300, 80, 20)),
            Item("bordo", new ScreenRect(280, 160, 40, 20))
        }.Where(p => !p.ScreenBounds.Intersect(crop).IsEmpty).Select(p => p.SourceText).ToArray();

        Assert.Contains("dentro", picked);
        Assert.Contains("bordo", picked);
        Assert.DoesNotContain("fuori", picked);
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

    private static OcrLine Line(string text, ScreenRect bounds) => new()
    {
        Text = text,
        Bounds = bounds,
        Confidence = 0.8f
    };

    private static ScreenRect Centered(int centerX, int y, int width, int height) =>
        new(centerX - width / 2, y, width, height);

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
