namespace Langu.Core;

public readonly record struct WindowsTextFit(double FontSize, bool FullLineInBox, bool Center);

public static class OverlayFont
{
    public const double CapRatio = 0.73;
    public const double DescentRatio = 0.23;

    public static double LineSize(double boxH, OcrBoxOrigin origin, string? source = null) =>
        origin == OcrBoxOrigin.Windows
            ? FitWindows(boxH, source ?? "", source ?? "").FontSize
            : Math.Clamp(boxH * 0.86, 7, 96);

    public static bool ShrinkToFitWidth(OcrBoxOrigin origin) =>
        origin != OcrBoxOrigin.Windows;

    public static WindowsTextFit FitWindows(double boxH, string source, string display)
    {
        var full = IncludesBelowInk(source);
        var em = full
            ? boxH / (CapRatio + DescentRatio)
            : boxH / CapRatio;
        return new WindowsTextFit(
            Math.Clamp(em * 0.96, 11, 96),
            full,
            CenterText(source, display));
    }

    public static bool IncludesBelowInk(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        return LanguageDetector.HasCjk(source) || HasLatinDescender(source);
    }

    public static bool HasLatinDescender(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case 'g' or 'j' or 'p' or 'q' or 'y':
                case 'Q' or 'ç' or 'Ç' or 'ý' or 'ÿ' or 'ỳ' or 'ỵ' or 'ỹ':
                    return true;
            }
        }

        return false;
    }

    public static bool CenterText(string source, string display) =>
        LanguageDetector.HasCjk(display)
        || LanguageDetector.HasCjk(source) && !LanguageDetector.IsMostlyLatin(display);

    public static ScreenRect GrowBox(ScreenRect box, string translated, string source, OcrBoxOrigin origin)
    {
        if (origin != OcrBoxOrigin.Windows || box.IsEmpty)
            return box;
        var text = string.IsNullOrWhiteSpace(translated) ? source : translated;
        if (string.IsNullOrWhiteSpace(text))
            return box;

        var fit = FitWindows(box.Height, source, text);
        var need = (int)Math.Ceiling(EstimateWidth(text, fit.FontSize)) + 4;
        if (need <= box.Width)
            return box;

        var extra = need - box.Width;
        var x = fit.Center ? box.X - extra / 2 : box.X;
        return new ScreenRect(x, box.Y, need, box.Height);
    }

    public static double EstimateWidth(string text, double fontSize)
    {
        double width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is '\n' or '\r')
                continue;
            width += c switch
            {
                <= 32 => fontSize * 0.30,
                'i' or 'l' or 'I' or 'j' or 'f' or 't' or 'r' or '\'' or '.' or ',' => fontSize * 0.32,
                'm' or 'M' or 'w' or 'W' => fontSize * 0.86,
                < 0x3000 => char.IsUpper((char)c) ? fontSize * 0.62 : fontSize * 0.52,
                _ => fontSize * 1.0
            };
        }

        return width;
    }
}
