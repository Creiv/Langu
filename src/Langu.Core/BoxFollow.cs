namespace Langu.Core;

public static class BoxFollow
{
    public static ScreenRect Follow(ScreenRect frozen, ScreenRect live, bool adoptSize)
    {
        if (live.IsEmpty)
            return frozen;
        if (frozen.IsEmpty || adoptSize)
            return live;

        var widthRatio = live.Width / (double)Math.Max(1, frozen.Width);
        var heightRatio = live.Height / (double)Math.Max(1, frozen.Height);
        if (widthRatio is >= 0.72 and <= 1.4 && heightRatio is >= 0.72 and <= 1.4)
            return live;

        if (frozen.IoU(live) < 0.2)
        {
            return new ScreenRect(
                live.CenterX - frozen.Width / 2,
                live.CenterY - frozen.Height / 2,
                frozen.Width,
                frozen.Height);
        }

        var dx = Math.Clamp(live.CenterX - frozen.CenterX, -Math.Max(4, frozen.Width / 5), Math.Max(4, frozen.Width / 5));
        var dy = Math.Clamp(live.CenterY - frozen.CenterY, -Math.Max(4, frozen.Height / 5), Math.Max(4, frozen.Height / 5));
        if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2)
            return frozen;
        return frozen.Offset(dx, dy);
    }
}
