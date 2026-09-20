namespace Langu.Core;

public sealed class TextAppearance
{
    public bool LightText { get; init; } = true;
    public bool Bold { get; init; }
    public bool Vertical { get; init; }
    public byte FillR { get; init; } = 245;
    public byte FillG { get; init; } = 242;
    public byte FillB { get; init; } = 236;
    public byte TextR { get; init; } = 72;
    public byte TextG { get; init; } = 48;
    public byte TextB { get; init; } = 28;

    public static TextAppearance Fallback(ScreenRect bounds) => new()
    {
        LightText = false,
        Bold = bounds.Height >= 26,
        Vertical = bounds.Height > bounds.Width * 1.45,
        FillR = 245,
        FillG = 242,
        FillB = 236,
        TextR = 72,
        TextG = 48,
        TextB = 28
    };

    public TextAppearance WithVertical(bool vertical) => new()
    {
        LightText = LightText,
        Bold = Bold,
        Vertical = vertical,
        FillR = FillR,
        FillG = FillG,
        FillB = FillB,
        TextR = TextR,
        TextG = TextG,
        TextB = TextB
    };

    public static TextAppearance Blend(IReadOnlyList<TextAppearance> items)
    {
        if (items.Count == 0)
            return Fallback(ScreenRect.Empty);
        if (items.Count == 1)
            return items[0];

        return new TextAppearance
        {
            LightText = items.Count(i => i.LightText) >= items.Count / 2,
            Bold = items.Count(i => i.Bold) >= items.Count / 2,
            Vertical = items.Count(i => i.Vertical) > items.Count / 2,
            FillR = Avg(items, i => i.FillR),
            FillG = Avg(items, i => i.FillG),
            FillB = Avg(items, i => i.FillB),
            TextR = Avg(items, i => i.TextR),
            TextG = Avg(items, i => i.TextG),
            TextB = Avg(items, i => i.TextB)
        };
    }

    public bool SimilarTo(TextAppearance other) =>
        ChannelDist(FillR, FillG, FillB, other.FillR, other.FillG, other.FillB) < 90
        && ChannelDist(TextR, TextG, TextB, other.TextR, other.TextG, other.TextB) < 110;

    public static TextAppearance FromFrame(CapturedFrame frame, ScreenRect imageBox, string? text = null)
    {
        if (frame.Width < 2 || frame.Height < 2 || imageBox.IsEmpty)
            return Fallback(imageBox);

        var x0 = Math.Clamp(imageBox.X, 0, frame.Width - 1);
        var y0 = Math.Clamp(imageBox.Y, 0, frame.Height - 1);
        var x1 = Math.Clamp(imageBox.X + imageBox.Width, x0 + 1, frame.Width);
        var y1 = Math.Clamp(imageBox.Y + imageBox.Height, y0 + 1, frame.Height);
        var pad = Math.Clamp(Math.Min(x1 - x0, y1 - y0) / 6, 3, 10);
        var ox0 = Math.Clamp(x0 - pad, 0, frame.Width - 1);
        var oy0 = Math.Clamp(y0 - pad, 0, frame.Height - 1);
        var ox1 = Math.Clamp(x1 + pad, ox0 + 1, frame.Width);
        var oy1 = Math.Clamp(y1 + pad, oy0 + 1, frame.Height);

        long ringR = 0, ringG = 0, ringB = 0, ringN = 0;
        long inkR = 0, inkG = 0, inkB = 0, inkN = 0;
        long lowR = 0, lowG = 0, lowB = 0, lowN = 0;
        long highR = 0, highG = 0, highB = 0, highN = 0;
        long sumL = 0;
        var innerN = 0;
        var stepY = Math.Max(1, (oy1 - oy0) / 36);
        var stepX = Math.Max(1, (ox1 - ox0) / 48);

        for (var y = oy0; y < oy1; y += stepY)
        {
            var row = y * frame.Stride;
            for (var x = ox0; x < ox1; x += stepX)
            {
                var i = row + x * 4;
                if (i + 2 >= frame.Bgra.Length)
                    continue;
                var b = frame.Bgra[i];
                var g = frame.Bgra[i + 1];
                var r = frame.Bgra[i + 2];
                var l = Luma(r, g, b);
                var inside = x >= x0 && x < x1 && y >= y0 && y < y1;
                if (!inside)
                {
                    ringR += r;
                    ringG += g;
                    ringB += b;
                    ringN++;
                    continue;
                }

                innerN++;
                sumL += l;
                if (l < 128)
                {
                    lowR += r;
                    lowG += g;
                    lowB += b;
                    lowN++;
                }
                else
                {
                    highR += r;
                    highG += g;
                    highB += b;
                    highN++;
                }
            }
        }

        byte fillR, fillG, fillB;
        if (ringN >= 6)
        {
            fillR = (byte)(ringR / ringN);
            fillG = (byte)(ringG / ringN);
            fillB = (byte)(ringB / ringN);
        }
        else if (lowN + highN > 0 && (lowN > highN * 2 || highN > lowN * 2))
        {
            if (lowN >= highN)
            {
                fillR = Avg(lowR, lowN);
                fillG = Avg(lowG, lowN);
                fillB = Avg(lowB, lowN);
            }
            else
            {
                fillR = Avg(highR, highN);
                fillG = Avg(highG, highN);
                fillB = Avg(highB, highN);
            }
        }
        else
        {
            fillR = 245;
            fillG = 242;
            fillB = 236;
        }

        var fillL = Luma(fillR, fillG, fillB);
        var inkThresh = 28;
        stepY = Math.Max(1, (y1 - y0) / 28);
        stepX = Math.Max(1, (x1 - x0) / 40);
        for (var y = y0; y < y1; y += stepY)
        {
            var row = y * frame.Stride;
            for (var x = x0; x < x1; x += stepX)
            {
                var i = row + x * 4;
                if (i + 2 >= frame.Bgra.Length)
                    continue;
                var b = frame.Bgra[i];
                var g = frame.Bgra[i + 1];
                var r = frame.Bgra[i + 2];
                if (Math.Abs(Luma(r, g, b) - fillL) < inkThresh)
                    continue;
                inkR += r;
                inkG += g;
                inkB += b;
                inkN++;
            }
        }

        byte textR, textG, textB;
        if (inkN >= 4)
        {
            textR = (byte)(inkR / inkN);
            textG = (byte)(inkG / inkN);
            textB = (byte)(inkB / inkN);
        }
        else if (lowN > 0 && highN > 0)
        {
            var useHigh = Math.Abs(Luma(Avg(highR, highN), Avg(highG, highN), Avg(highB, highN)) - fillL)
                          >= Math.Abs(Luma(Avg(lowR, lowN), Avg(lowG, lowN), Avg(lowB, lowN)) - fillL);
            if (useHigh)
            {
                textR = Avg(highR, highN);
                textG = Avg(highG, highN);
                textB = Avg(highB, highN);
            }
            else
            {
                textR = Avg(lowR, lowN);
                textG = Avg(lowG, lowN);
                textB = Avg(lowB, lowN);
            }
        }
        else
        {
            textR = fillL > 140 ? (byte)72 : (byte)245;
            textG = fillL > 140 ? (byte)48 : (byte)242;
            textB = fillL > 140 ? (byte)28 : (byte)236;
        }

        var textL = Luma(textR, textG, textB);
        if (Math.Abs(textL - fillL) < 36)
        {
            if (fillL >= 128)
            {
                textR = Push(textR, 48);
                textG = Push(textG, 32);
                textB = Push(textB, 20);
            }
            else
            {
                textR = Push(textR, 236);
                textG = Push(textG, 232);
                textB = Push(textB, 224);
            }
        }

        var cjk = !string.IsNullOrWhiteSpace(text) && LanguageDetector.HasCjk(text);
        var vertical = imageBox.Height >= imageBox.Width * 1.45
                       && (cjk || imageBox.Width < imageBox.Height * 0.5);

        return new TextAppearance
        {
            LightText = Luma(textR, textG, textB) > Luma(fillR, fillG, fillB),
            Bold = imageBox.Height >= 24 || cjk && imageBox.Height >= 16,
            Vertical = vertical,
            FillR = fillR,
            FillG = fillG,
            FillB = fillB,
            TextR = textR,
            TextG = textG,
            TextB = textB
        };
    }

    private static int Luma(int r, int g, int b) => (r * 30 + g * 59 + b * 11) / 100;

    private static byte Avg(long sum, long n) => n <= 0 ? (byte)0 : (byte)(sum / n);

    private static byte Avg(IReadOnlyList<TextAppearance> items, Func<TextAppearance, byte> pick) =>
        (byte)Math.Clamp((int)items.Average(i => (double)pick(i)), 0, 255);

    private static int ChannelDist(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
        Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);

    private static byte Push(byte value, byte target) =>
        (byte)Math.Clamp((value * 2 + target * 3) / 5, 0, 255);
}
