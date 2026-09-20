namespace Langu.Core;

public static class BoxFollow
{
    public static ScreenRect Follow(ScreenRect frozen, ScreenRect live, bool adoptSize)
    {
        if (live.IsEmpty)
            return frozen;
        if (frozen.IsEmpty || adoptSize)
            return live;

        var dx = live.CenterX - frozen.CenterX;
        var dy = live.CenterY - frozen.CenterY;
        var iou = frozen.IoU(live);
        if (iou >= 0.45 && Math.Abs(dx) <= 10 && Math.Abs(dy) <= 8)
            return frozen;
        if (iou < 0.28)
            return frozen;

        var stepX = Math.Clamp(dx, -Math.Max(6, frozen.Width / 4), Math.Max(6, frozen.Width / 4));
        var stepY = Math.Clamp(dy, -Math.Max(6, frozen.Height / 4), Math.Max(6, frozen.Height / 4));
        if (Math.Abs(stepX) < 3 && Math.Abs(stepY) < 3)
            return frozen;
        return frozen.Offset(stepX, stepY);
    }
}
