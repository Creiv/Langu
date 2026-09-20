namespace Langu.Core;

public sealed class AppSettings
{
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Monitor;
    public TargetLanguage TargetLanguage { get; set; } = TargetLanguage.Italian;
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Auto;
    public string? SourceLanguage { get; set; }
    public int TargetMonitorIndex { get; set; }
    public long TargetWindowHandle { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; } = 800;
    public int RegionHeight { get; set; } = 240;
    public int FramesPerSecond { get; set; } = 3;
    public double MinOcrConfidence { get; set; } = 0.20;
    public bool OverlayEnabled { get; set; } = true;
    public int ProbeKeyVk { get; set; } = ProbeKeys.DefaultVk;

    public ScreenRect RegionBounds => new(RegionX, RegionY, RegionWidth, RegionHeight);

    public static string TargetNllbCode(TargetLanguage language) =>
        language == TargetLanguage.Italian ? "ita_Latn" : "eng_Latn";

    public static string TargetIso(TargetLanguage language) =>
        language == TargetLanguage.Italian ? "it" : "en";
}
