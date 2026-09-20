namespace Langu.Core;

public static class SourceLanguages
{
    public static readonly (string? Iso, string Name)[] Choices =
    [
        (null, "Rileva automaticamente"),
        ("ja", "Giapponese"),
        ("zh", "Cinese"),
        ("ko", "Coreano"),
        ("en", "Inglese"),
        ("it", "Italiano"),
        ("fr", "Francese"),
        ("de", "Tedesco"),
        ("es", "Spagnolo"),
        ("ru", "Russo"),
        ("ar", "Arabo")
    ];
}
