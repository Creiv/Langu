namespace Langu.Core;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public static ScreenRect Empty => new(0, 0, 0, 0);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public ScreenRect Union(ScreenRect other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        var x1 = Math.Min(X, other.X);
        var y1 = Math.Min(Y, other.Y);
        var x2 = Math.Max(X + Width, other.X + other.Width);
        var y2 = Math.Max(Y + Height, other.Y + other.Height);
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }

    public ScreenRect Intersect(ScreenRect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(X + Width, other.X + other.Width);
        var y2 = Math.Min(Y + Height, other.Y + other.Height);
        return x2 > x1 && y2 > y1
            ? new ScreenRect(x1, y1, x2 - x1, y2 - y1)
            : Empty;
    }

    public ScreenRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public ScreenRect Inflate(int dx, int dy) =>
        new(X - dx, Y - dy, Math.Max(0, Width + dx * 2), Math.Max(0, Height + dy * 2));

    public ScreenRect Pad(int left, int top, int right, int bottom) =>
        new(X - left, Y - top, Math.Max(1, Width + left + right), Math.Max(1, Height + top + bottom));

    public ScreenRect ExpandForGlyphs()
    {
        if (IsEmpty)
            return this;
        var padX = Math.Max(3, (int)Math.Ceiling(Height * 0.11 + Width * 0.012));
        var padTop = Math.Max(2, (int)Math.Ceiling(Height * 0.18));
        var padBot = Math.Max(3, (int)Math.Ceiling(Height * 0.26));
        return Pad(padX, padTop, padX, padBot);
    }

    public bool Contains(int x, int y) =>
        x >= X && y >= Y && x < X + Width && y < Y + Height;

    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public int Area => Math.Max(0, Width) * Math.Max(0, Height);

    public double IoU(ScreenRect other)
    {
        var i = Intersect(other);
        if (i.IsEmpty)
            return 0;
        var union = Area + other.Area - i.Area;
        return union <= 0 ? 0 : i.Area / (double)union;
    }

    public bool Overlaps(ScreenRect other, double minShare = 0.4)
    {
        var i = Intersect(other);
        if (i.IsEmpty)
            return false;
        var min = Math.Min(Area, other.Area);
        return min > 0 && i.Area / (double)min >= minShare;
    }
}

public readonly record struct LanguageGuess(string Iso639, string NllbCode, float Confidence);
