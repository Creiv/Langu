namespace Langu.Core;

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct TextQuad
{
    public ScreenPoint P0 { get; init; }
    public ScreenPoint P1 { get; init; }
    public ScreenPoint P2 { get; init; }
    public ScreenPoint P3 { get; init; }

    public static TextQuad Empty => default;
    public bool IsValid => Length >= 1 && Thickness >= 1;

    public int CenterX => (P0.X + P1.X + P2.X + P3.X) / 4;
    public int CenterY => (P0.Y + P1.Y + P2.Y + P3.Y) / 4;

    public double Length
    {
        get
        {
            var dx = P1.X - P0.X;
            var dy = P1.Y - P0.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public double Thickness
    {
        get
        {
            var dx = P3.X - P0.X;
            var dy = P3.Y - P0.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public double AngleRadians => Math.Atan2(P1.Y - P0.Y, P1.X - P0.X);

    public double AngleDegrees
    {
        get
        {
            var angle = AngleRadians * (180 / Math.PI);
            while (angle > 90)
                angle -= 180;
            while (angle < -90)
                angle += 180;
            return angle;
        }
    }

    public TextQuad Expand(double along, double across)
    {
        if (!IsValid)
            return FromRect(Bounds.ExpandForGlyphs());
        return FromCenter(CenterX, CenterY, Length + along * 2, Thickness + across * 2, AngleRadians);
    }

    public ScreenRect Bounds
    {
        get
        {
            var minX = Math.Min(Math.Min(P0.X, P1.X), Math.Min(P2.X, P3.X));
            var minY = Math.Min(Math.Min(P0.Y, P1.Y), Math.Min(P2.Y, P3.Y));
            var maxX = Math.Max(Math.Max(P0.X, P1.X), Math.Max(P2.X, P3.X));
            var maxY = Math.Max(Math.Max(P0.Y, P1.Y), Math.Max(P2.Y, P3.Y));
            return new ScreenRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
    }

    public bool IsTilted
    {
        get
        {
            if (!IsValid)
                return false;
            var angle = Math.Abs(AngleDegrees);
            if (angle is < 14 or > 76)
                return false;
            return Bounds.Width < Bounds.Height * 2.5 || angle >= 22;
        }
    }

    public static TextQuad FlattenLevel(TextQuad quad, ScreenRect bounds)
    {
        if (bounds.IsEmpty)
            return quad;
        if (!quad.IsValid || !quad.IsTilted)
            return FromRect(bounds);
        return quad;
    }

    public static TextQuad FromRect(ScreenRect rect)
    {
        if (rect.IsEmpty)
            return Empty;
        return new TextQuad
        {
            P0 = new ScreenPoint(rect.X, rect.Y),
            P1 = new ScreenPoint(rect.X + rect.Width, rect.Y),
            P2 = new ScreenPoint(rect.X + rect.Width, rect.Y + rect.Height),
            P3 = new ScreenPoint(rect.X, rect.Y + rect.Height)
        };
    }

    public static TextQuad FromCenter(double cx, double cy, double length, double thickness, double angleRadians)
    {
        length = Math.Max(1, length);
        thickness = Math.Max(1, thickness);
        var ux = Math.Cos(angleRadians) * length / 2;
        var uy = Math.Sin(angleRadians) * length / 2;
        var vx = -Math.Sin(angleRadians) * thickness / 2;
        var vy = Math.Cos(angleRadians) * thickness / 2;
        return Normalize(new TextQuad
        {
            P0 = Pt(cx - ux - vx, cy - uy - vy),
            P1 = Pt(cx + ux - vx, cy + uy - vy),
            P2 = Pt(cx + ux + vx, cy + uy + vy),
            P3 = Pt(cx - ux + vx, cy - uy + vy)
        });
    }

    public static TextQuad FromPoints(IReadOnlyList<ScreenPoint> points)
    {
        if (points.Count < 4)
            return Empty;

        var cx = points.Take(4).Average(p => (double)p.X);
        var cy = points.Take(4).Average(p => (double)p.Y);
        var ordered = points.Take(4)
            .OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx))
            .ToArray();

        var best = 0;
        var bestLen = -1.0;
        for (var i = 0; i < 4; i++)
        {
            var len = Dist(ordered[i], ordered[(i + 1) % 4]);
            if (len <= bestLen)
                continue;
            bestLen = len;
            best = i;
        }

        return Normalize(new TextQuad
        {
            P0 = ordered[best],
            P1 = ordered[(best + 1) % 4],
            P2 = ordered[(best + 2) % 4],
            P3 = ordered[(best + 3) % 4]
        });
    }

    public static TextQuad FromWordBoxes(IReadOnlyList<ScreenRect> words)
    {
        if (words.Count == 0)
            return Empty;
        if (words.Count == 1)
            return FromRect(words[0]);

        var first = words[0];
        var last = words[^1];
        var dx = last.CenterX - first.CenterX;
        var dy = last.CenterY - first.CenterY;
        if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
            return FromRect(Union(words));

        var vertical = Math.Abs(dy) > Math.Abs(dx) * 1.15;
        var thickness = words.Average(w => vertical ? w.Width : w.Height);
        var extra = words.Average(w => vertical ? w.Height : w.Width);
        var length = Math.Sqrt(dx * dx + dy * dy) + extra;
        var angle = Math.Atan2(dy, dx);
        var cx = words.Average(w => (double)w.CenterX);
        var cy = words.Average(w => (double)w.CenterY);
        return FromCenter(cx, cy, length, Math.Max(6, thickness), angle);
    }

    public TextQuad Map(Func<ScreenPoint, ScreenPoint> map) =>
        Normalize(new TextQuad
        {
            P0 = map(P0),
            P1 = map(P1),
            P2 = map(P2),
            P3 = map(P3)
        });

    public TextQuad Offset(int dx, int dy) =>
        IsValid ? Map(p => new ScreenPoint(p.X + dx, p.Y + dy)) : this;

    public bool Contains(int x, int y, double pad = 0)
    {
        if (!IsValid)
            return Bounds.Inflate((int)Math.Ceiling(pad), (int)Math.Ceiling(pad)).Contains(x, y);

        var ux = P1.X - P0.X;
        var uy = P1.Y - P0.Y;
        var vx = P3.X - P0.X;
        var vy = P3.Y - P0.Y;
        var px = x - P0.X;
        var py = y - P0.Y;
        var uu = ux * ux + uy * uy;
        var vv = vx * vx + vy * vy;
        if (uu < 1 || vv < 1)
            return Bounds.Contains(x, y);

        var s = (px * ux + py * uy) / uu;
        var t = (px * vx + py * vy) / vv;
        var ps = pad / Math.Sqrt(uu);
        var pt = pad / Math.Sqrt(vv);
        return s >= -ps && s <= 1 + ps && t >= -pt && t <= 1 + pt;
    }

    private static TextQuad Normalize(TextQuad quad)
    {
        if (quad.Thickness > quad.Length && quad.Length >= 1)
        {
            return new TextQuad
            {
                P0 = quad.P0,
                P1 = quad.P3,
                P2 = quad.P2,
                P3 = new ScreenPoint(quad.P0.X - (quad.P1.X - quad.P0.X), quad.P0.Y - (quad.P1.Y - quad.P0.Y))
            };
        }

        return quad;
    }

    private static ScreenRect Union(IReadOnlyList<ScreenRect> boxes)
    {
        var x1 = boxes.Min(b => b.X);
        var y1 = boxes.Min(b => b.Y);
        var x2 = boxes.Max(b => b.X + b.Width);
        var y2 = boxes.Max(b => b.Y + b.Height);
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static double Dist(ScreenPoint a, ScreenPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static ScreenPoint Pt(double x, double y) =>
        new((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Round(y, MidpointRounding.AwayFromZero));
}
