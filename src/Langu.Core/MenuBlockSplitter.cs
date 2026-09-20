namespace Langu.Core;

public static class MenuBlockSplitter
{
    public static List<OverlayItem> Split(IReadOnlyList<OverlayItem> items)
    {
        var result = new List<OverlayItem>();
        foreach (var item in items)
        {
            if (!TrySplit(item, out var parts))
            {
                result.Add(item);
                continue;
            }

            result.AddRange(parts);
        }

        return result;
    }

    private static bool TrySplit(OverlayItem item, out List<OverlayItem> parts)
    {
        parts = [];
        var raw = item.SourceText.Replace("\r\n", "\n").Trim();
        var box = item.ScreenBounds;
        List<string> lines;
        if (raw.Contains('\n'))
        {
            lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        else if (GameUiGlossary.TrySegment(raw, out var segmented) && segmented.Count >= 2 && segmented.All(UiText.IsCompactUiLabel))
        {
            lines = segmented;
        }
        else
        {
            return false;
        }

        if (lines.Count < 2)
            return false;

        var minHeight = 30 * lines.Count;
        if (box.Height < minHeight)
            return false;
        if (box.Height / (double)Math.Max(1, box.Width) < 0.55 && !raw.Contains('\n'))
            return false;

        var slices = SliceVertical(box, lines);
        parts = new List<OverlayItem>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var slice = slices[i];
            parts.Add(item with
            {
                Id = $"{lines[i]}|{slice.X / 8}:{slice.Y / 8}:{slice.Width / 8}:{slice.Height / 8}",
                SourceText = lines[i],
                ScreenBounds = slice,
                Quad = TextQuad.FromRect(slice)
            });
        }

        return true;
    }

    private static List<ScreenRect> SliceVertical(ScreenRect box, IReadOnlyList<string> parts)
    {
        var weights = parts.Select(p => Math.Max(1, p.Length)).ToArray();
        var total = weights.Sum();
        var slices = new List<ScreenRect>(parts.Count);
        var cursor = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var share = i == parts.Count - 1
                ? box.Height - cursor
                : Math.Max(12, (int)Math.Round(box.Height * (weights[i] / (double)total)));
            slices.Add(new ScreenRect(box.X, box.Y + cursor, box.Width, share));
            cursor += share;
        }

        return slices;
    }
}
