namespace Langu.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Langu");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ModelsRoot => Path.Combine(Root, "models");
    public static string OcrModelDir => Path.Combine(ModelsRoot, "ocr");
    public static string NllbModelDir => Path.Combine(ModelsRoot, "nllb");
    public static string FastTextModel => Path.Combine(ModelsRoot, "lid.176.ftz");
    public static string RuntimeRoot => Path.Combine(Root, "runtime");
    public static string PythonDir => Path.Combine(RuntimeRoot, "python");
    public static string PythonExe => Path.Combine(PythonDir, "python.exe");
    public static string WorkerScript => Path.Combine(RuntimeRoot, "translator_worker.py");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ModelsRoot);
        Directory.CreateDirectory(OcrModelDir);
        Directory.CreateDirectory(NllbModelDir);
        Directory.CreateDirectory(RuntimeRoot);
        Directory.CreateDirectory(PythonDir);
    }
}
