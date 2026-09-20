namespace Langu.Core;

public static class SourceLanguages
{
    public static readonly (string? Iso, string Name)[] Choices =
    [
        (null, "Auto-detect"),
        ("ja", "Japanese"),
        ("zh", "Chinese"),
        ("ko", "Korean"),
        ("en", "English"),
        ("it", "Italian"),
        ("fr", "French"),
        ("de", "German"),
        ("es", "Spanish"),
        ("ru", "Russian"),
        ("ar", "Arabic")
    ];
}
