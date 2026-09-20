using System.Collections.Concurrent;

namespace Langu.Core;

public sealed class TranslationCache
{
    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.Ordinal);

    public bool TryGet(string text, TargetLanguage target, out string translated) =>
        _map.TryGetValue(Key(text, target), out translated!);

    public void Set(string text, TargetLanguage target, string translated) =>
        _map[Key(text, target)] = translated;

    public void Clear() => _map.Clear();

    private static string Key(string text, TargetLanguage target) =>
        target + "\u001f" + text.Trim();
}
