namespace Langu.Core;

public enum ReadingKind
{
    Line,
    Menu,
    Sentence
}

public sealed record ReadingGroup(ReadingKind Kind, IReadOnlyList<OverlayItem> Members);

public static class OcrReadingLayout
{
    public static List<ReadingGroup> Group(IReadOnlyList<OverlayItem> lines)
    {
        if (lines.Count == 0)
            return [];

        var ordered = lines
            .OrderBy(i => i.ScreenBounds.Y)
            .ThenBy(i => i.ScreenBounds.X)
            .ToList();
        var used = new bool[ordered.Count];
        var groups = new List<ReadingGroup>();

        MarkMenuColumns(ordered, used, groups);

        for (var i = 0; i < ordered.Count; i++)
        {
            if (used[i])
                continue;

            var cluster = new List<OverlayItem> { ordered[i] };
            used[i] = true;
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (used[j])
                    continue;
                if (!ContinuesSentence(cluster[^1], ordered[j]))
                    break;
                cluster.Add(ordered[j]);
                used[j] = true;
            }

            groups.Add(new ReadingGroup(
                cluster.Count > 1 ? ReadingKind.Sentence : ReadingKind.Line,
                cluster));
        }

        return groups
            .OrderBy(g => g.Members[0].ScreenBounds.Y)
            .ThenBy(g => g.Members[0].ScreenBounds.X)
            .ToList();
    }

    public static string JoinSources(IReadOnlyList<OverlayItem> members)
    {
        var cjk = members.Count(m => LanguageDetector.HasCjk(m.SourceText)) >= (members.Count + 1) / 2;
        return string.Join(cjk ? "" : " ", members.Select(m => m.SourceText.Trim()).Where(t => t.Length > 0));
    }

    public static IReadOnlyList<string> Allocate(string translated, IReadOnlyList<string> sources)
    {
        if (sources.Count <= 1)
            return [translated.Trim()];

        var words = translated.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return sources.Select(_ => "").ToList();
        if (words.Length <= sources.Count)
        {
            var padded = new string[sources.Count];
            for (var i = 0; i < sources.Count; i++)
                padded[i] = i < words.Length ? words[i] : "";
            if (words.Length < sources.Count)
                padded[sources.Count - 1] = string.Join(" ", words.Skip(Math.Max(0, words.Length - 1)));
            return padded;
        }

        var weights = sources.Select(s => Math.Max(1, s.Trim().Length)).ToArray();
        var total = weights.Sum();
        var result = new List<string>(sources.Count);
        var cursor = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            var take = i == sources.Count - 1
                ? words.Length - cursor
                : Math.Max(1, (int)Math.Round(words.Length * (weights[i] / (double)total)));
            take = Math.Clamp(take, 1, words.Length - cursor - (sources.Count - i - 1));
            result.Add(string.Join(" ", words.Skip(cursor).Take(take)));
            cursor += take;
        }

        return result;
    }

    private static void MarkMenuColumns(List<OverlayItem> ordered, bool[] used, List<ReadingGroup> groups)
    {
        var candidates = new List<int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (LooksLikeMenuLabel(ordered[i].SourceText))
                candidates.Add(i);
        }

        if (candidates.Count < 3)
            return;

        var leftover = candidates.ToList();
        while (leftover.Count >= 3)
        {
            var seed = leftover[0];
            leftover.RemoveAt(0);
            var column = new List<int> { seed };
            for (var i = leftover.Count - 1; i >= 0; i--)
            {
                if (SameMenuColumn(ordered[seed], ordered[leftover[i]]))
                {
                    column.Add(leftover[i]);
                    leftover.RemoveAt(i);
                }
            }

            if (column.Count < 3)
                continue;

            column.Sort();
            foreach (var index in column)
            {
                used[index] = true;
                groups.Add(new ReadingGroup(ReadingKind.Menu, [ordered[index]]));
            }
        }
    }

    private static bool SameMenuColumn(OverlayItem a, OverlayItem b)
    {
        var left = a.ScreenBounds;
        var right = b.ScreenBounds;
        var minW = Math.Max(1, Math.Min(left.Width, right.Width));
        var center = Math.Abs(left.CenterX - right.CenterX) <= Math.Max(12, minW * 0.35);
        var heightOk = Math.Abs(left.Height - right.Height) <= Math.Max(8, Math.Max(left.Height, right.Height) * 0.45);
        return center && heightOk && LooksLikeMenuLabel(a.SourceText) && LooksLikeMenuLabel(b.SourceText);
    }

    public static bool LooksLikeMenuLabel(string text)
    {
        var t = text.Trim();
        if (t.Length is < 1 or > 16)
            return false;
        if (t.IndexOfAny(['。', '！', '？', '.', '!', '?']) >= 0)
            return false;
        if (t.Contains("ありがとう", StringComparison.Ordinal)
            || t.Contains("ください", StringComparison.Ordinal)
            || t.EndsWith("です", StringComparison.Ordinal)
            || t.EndsWith("ます", StringComparison.Ordinal))
            return false;
        if (GameUiGlossary.TryTranslate(t, "it", out _))
            return true;
        if (LanguageDetector.HasCjk(t) && t.EndsWith('戦') && t.Length <= 16)
            return true;
        if (UiText.IsCompactUiLabel(t))
            return true;
        if (LanguageDetector.IsMostlyLatin(t))
            return t.Length is >= 2 and <= 8 && t.All(c => !char.IsLetter(c) || char.IsUpper(c)) && !t.Contains(' ');
        return t.Length <= 8 && !t.Contains(' ');
    }

    public static bool ContinuesSentence(OverlayItem previous, OverlayItem next)
    {
        if (LooksLikeMenuLabel(previous.SourceText) || LooksLikeMenuLabel(next.SourceText))
            return false;
        if (previous.Shape.IsTilted || next.Shape.IsTilted)
            return false;

        var a = previous.ScreenBounds;
        var b = next.ScreenBounds;
        var h = Math.Max(1, (a.Height + b.Height) / 2.0);
        if (b.Y < a.Y + a.Height - 4)
            return false;
        var gap = b.Y - (a.Y + a.Height);
        if (gap > h * 0.95)
            return false;
        if (Math.Abs(a.Height - b.Height) > h * 0.55)
            return false;

        var left = Math.Abs(a.X - b.X) <= Math.Max(10, Math.Min(a.Width, b.Width) * 0.18);
        if (!left)
            return false;
        if (EndsComplete(previous.SourceText))
            return false;
        return true;
    }

    private static bool EndsComplete(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
            return true;
        return t.EndsWith('。') || t.EndsWith('！') || t.EndsWith('？')
               || t.EndsWith('.') || t.EndsWith('!') || t.EndsWith('?');
    }
}
