namespace Langu.Core;

public static class ProbeKeys
{
    public const int DefaultVk = 0xA4;

    public static readonly (int Vk, string Name)[] Choices =
    [
        (0xA4, "Alt sinistro"),
        (0xA5, "Alt destro"),
        (0xA2, "Ctrl sinistro"),
        (0xA3, "Ctrl destro"),
        (0x10, "Shift"),
        (0x14, "Bloc Maiusc"),
        (0xC0, "Accento `"),
        (0x71, "F2"),
        (0x72, "F3"),
        (0x09, "Tab"),
        (0x20, "Spazio"),
        (0x58, "X"),
        (0x43, "C"),
        (0x56, "V")
    ];

    public static string NameOf(int vk)
    {
        foreach (var (code, name) in Choices)
        {
            if (code == vk)
                return name;
        }

        return vk is >= 0x41 and <= 0x5A
            ? ((char)vk).ToString()
            : $"Tasto 0x{vk:X2}";
    }

    public static bool Matches(int configured, int pressed, uint flags)
    {
        if (pressed == configured)
            return true;

        var extended = (flags & 1) != 0;
        return configured switch
        {
            0xA4 => (pressed is 0xA4 or 0x12) && !extended,
            0xA5 => (pressed is 0xA5 or 0x12) && extended,
            0xA2 => (pressed is 0xA2 or 0x11) && !extended,
            0xA3 => (pressed is 0xA3 or 0x11) && extended,
            0x10 => pressed is 0x10 or 0xA0 or 0xA1,
            _ => false
        };
    }
}
