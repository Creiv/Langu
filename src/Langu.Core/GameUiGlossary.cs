namespace Langu.Core;

public static class GameUiGlossary
{
    private static readonly Dictionary<string, (string It, string En)> Map = new(StringComparer.Ordinal)
    {
        ["ストーリー"] = ("Storia", "Story"),
        ["外伝ストーリー"] = ("Storia extra", "Gaiden story"),
        ["外伝"] = ("Extra", "Gaiden"),
        ["天下統一"] = ("Unificazione", "Unification"),
        ["自由合戦"] = ("Battaglia libera", "Free battle"),
        ["対戦"] = ("Scontro", "Versus"),
        ["大乱闘"] = ("Scontro", "Smash"),
        ["生存"] = ("Sopravvivi", "Survive"),
        ["ガイド"] = ("Guida", "Guide"),
        ["説明"] = ("Guida", "Help"),
        ["せつめい"] = ("Guida", "Help"),
        ["もどる"] = ("Indietro", "Back"),
        ["せんたく"] = ("Seleziona", "Select"),
        ["ファイター"] = ("Combattente", "Fighter"),
        ["スピリッツ"] = ("Spiriti", "Spirits"),
        ["オンライン"] = ("Online", "Online"),
        ["ショップ"] = ("Negozio", "Shop"),
        ["冒険"] = ("Avventura", "Adventure"),
        ["やめる"] = ("Esci", "Quit"),
        ["一心同体戦"] = ("Battaglia unita", "Unity battle"),
        ["達磨叩き戦"] = ("Battaglia Daruma", "Daruma battle"),
        ["超人魂争奪戦"] = ("Rissa delle anime", "Soul scramble"),
        ["超人魂争奪戦~其の弐~"] = ("Rissa delle anime 2", "Soul scramble 2"),
        ["超人魂争奪戦其の弐"] = ("Rissa delle anime 2", "Soul scramble 2"),
        ["大軍撃破戦"] = ("Sconfiggi l'esercito", "Army assault"),
        ["最強決定戦"] = ("Finale del più forte", "Strongest match"),
        ["生存競争戦"] = ("Sopravvivenza", "Survival"),
        ["最大連撃戦"] = ("Combo massima", "Max combo"),
        ["解説"] = ("Guida", "Help"),
        ["大武闘会"] = ("Torneo", "Tournament"),
        ["ギャラリー"] = ("Galleria", "Gallery"),
        ["各種設定"] = ("Impostazioni", "Settings"),
        ["設定"] = ("Impostazioni", "Settings"),
        ["選択"] = ("Seleziona", "Select"),
        ["決定"] = ("Conferma", "Confirm"),
        ["戻る"] = ("Indietro", "Back"),
        ["オプション"] = ("Opzioni", "Options"),
        ["セーブ"] = ("Salva", "Save"),
        ["ロード"] = ("Carica", "Load"),
        ["ニューゲーム"] = ("Nuova partita", "New game"),
        ["コンティニュー"] = ("Continua", "Continue"),
        ["開始"] = ("Inizia", "Start"),
        ["終了"] = ("Esci", "Exit"),
        ["はい"] = ("Sì", "Yes"),
        ["いいえ"] = ("No", "No"),
        ["確認"] = ("Conferma", "Confirm"),
        ["キャンセル"] = ("Annulla", "Cancel"),
        ["タイトル"] = ("Titolo", "Title"),
        ["メニュー"] = ("Menu", "Menu"),
        ["マップ"] = ("Mappa", "Map"),
        ["ステータス"] = ("Stato", "Status"),
        ["アイテム"] = ("Oggetti", "Items"),
        ["装備"] = ("Equipaggiamento", "Equipment"),
        ["スキル"] = ("Abilità", "Skills"),
        ["魔法"] = ("Magie", "Magic"),
        ["技"] = ("Tecniche", "Techniques"),
        ["会話"] = ("Dialogo", "Talk"),
        ["作戦"] = ("Strategia", "Tactics"),
        ["準備"] = ("Preparazione", "Prepare"),
        ["出撃"] = ("Sortita", "Sortie"),
        ["撤退"] = ("Ritirata", "Retreat"),
        ["勝利"] = ("Vittoria", "Victory"),
        ["敗北"] = ("Sconfitta", "Defeat"),
        ["次へ"] = ("Avanti", "Next"),
        ["閉じる"] = ("Chiudi", "Close"),
        ["遊び方"] = ("Come si gioca", "How to play"),
        ["クレジット"] = ("Riconoscimenti", "Credits"),
        ["音楽"] = ("Musica", "Music"),
        ["音声"] = ("Audio", "Audio"),
        ["画面"] = ("Schermo", "Display"),
        ["難易度"] = ("Difficoltà", "Difficulty"),
        ["簡単"] = ("Facile", "Easy"),
        ["普通"] = ("Normale", "Normal"),
        ["難しい"] = ("Difficile", "Hard")
    };

    private static readonly string[] Keys = Map.Keys.OrderByDescending(k => k.Length).ToArray();

    public static bool TryTranslate(string text, string targetIso, out string translated)
    {
        translated = "";
        var normalized = UiText.NormalizeSource(text);
        if (normalized.Length == 0)
            return false;
        if (Map.TryGetValue(normalized, out var pair))
        {
            translated = Pick(pair, targetIso);
            return true;
        }

        if (!TrySegment(normalized, out var parts) || parts.Count == 0)
            return false;
        if (parts.Any(p => !Map.ContainsKey(p)))
            return false;

        translated = string.Join(" ", parts.Select(p => Pick(Map[p], targetIso)));
        return translated.Length > 0;
    }

    public static bool TrySegment(string text, out List<string> parts)
    {
        parts = [];
        var normalized = UiText.NormalizeSource(text);
        if (normalized.Length == 0)
            return false;

        foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = 0;
            var lineParts = new List<string>();
            while (index < line.Length)
            {
                var hit = Keys.FirstOrDefault(key =>
                    index + key.Length <= line.Length &&
                    string.CompareOrdinal(line, index, key, 0, key.Length) == 0);
                if (hit is null)
                    return false;
                lineParts.Add(hit);
                index += hit.Length;
            }

            if (lineParts.Count == 0)
                return false;
            parts.AddRange(lineParts);
        }

        return parts.Count > 0;
    }

    private static string Pick((string It, string En) pair, string targetIso) =>
        string.Equals(targetIso, "en", StringComparison.OrdinalIgnoreCase) ? pair.En : pair.It;
}
