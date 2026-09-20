using System.Text.Json;

namespace Langu.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsFile))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
