using System.Text.Json;
using System.Text.RegularExpressions;

namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// YMM4 の読み上げ変換ユーザー辞書 (KanjiToYomi.UserDictionary.json) を直接読み込み、
/// セリフへのテキスト置換として適用する。
///
/// YMM4 は Text 形式プラグインにはセリフをそのまま渡すため、YMM4 内部の辞書は適用されない。
/// その代わりに YMM4 と同じ辞書ファイルを読み込んで同じ置換を行い、
/// 「YMM4 の辞書がこのプラグインでも効く」状態を再現する。
///
/// 辞書ファイルが見つからない・壊れている場合は何も置換せず元テキストを返す
/// (劣化フォールバック。YMM4 のバージョン更新でパスや形式が変わってもプラグインは動き続ける)。
/// </summary>
public static class YmmUserDictionary
{
    sealed record Entry(string From, string To, bool IsRegex, bool IgnoreCase);

    static readonly object Lock = new();
    static string? cachedPath;
    static DateTime cachedLastWriteUtc;
    static List<Entry>? cachedEntries;

    /// <summary>YMM4 のユーザー辞書をテキストへ適用する (辞書が無ければ元のまま)。</summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var entries = GetEntries();
        if (entries is null || entries.Count == 0) return text;

        foreach (var e in entries)
        {
            try
            {
                var options = e.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                string pattern = e.IsRegex ? e.From : Regex.Escape(e.From);
                // 正規表現エントリは $1 等の後方参照を許可し、リテラルは $ をエスケープする
                string replacement = e.IsRegex ? e.To : e.To.Replace("$", "$$");
                text = Regex.Replace(text, pattern, replacement, options);
            }
            catch
            {
                // 個別エントリの失敗は無視する (他のエントリは適用し続ける)
            }
        }
        return text;
    }

    static List<Entry>? GetEntries()
    {
        string? path = FindDictionaryPath();
        if (path is null) return null;

        DateTime lastWriteUtc;
        try { lastWriteUtc = File.GetLastWriteTimeUtc(path); }
        catch { return cachedEntries; }

        lock (Lock)
        {
            if (cachedPath == path && cachedLastWriteUtc == lastWriteUtc)
                return cachedEntries;

            cachedPath = path;
            cachedLastWriteUtc = lastWriteUtc;
            cachedEntries = Load(path);
            return cachedEntries;
        }
    }

    /// <summary>
    /// プラグインの配置先 (&lt;YMM4&gt;\user\plugin\VOICEROID2-YMM\) から YMM4 ルートを特定し、
    /// user\setting 配下の最新のユーザー辞書 JSON を探す。
    /// </summary>
    static string? FindDictionaryPath()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 4 && dir is not null; i++, dir = dir.Parent)
            {
                if (dir is null) break;
                string settingRoot = Path.Combine(dir.FullName, "user", "setting");
                if (!Directory.Exists(settingRoot)) continue;

                var newest = Directory
                    .EnumerateFiles(settingRoot, "YukkuriMovieMaker.KanjiToYomi.UserDictionary.json", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest is not null) return newest.FullName;
            }
        }
        catch
        {
            // 辞書の解決失敗は無視 (辞書なしで動作)
        }
        return null;
    }

    static List<Entry>? Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<Entry>();
            foreach (string prop in new[] { "WordSets", "AsteriskWordSets" })
            {
                if (!doc.RootElement.TryGetProperty(prop, out var sets)) continue;

                foreach (var e in sets.EnumerateArray())
                {
                    if (!e.TryGetProperty("IsEnabled", out var enabled) || !enabled.GetBoolean()) continue;
                    string from = GetString(e, "From");
                    if (string.IsNullOrEmpty(from)) continue;
                    list.Add(new Entry(from, GetString(e, "To"), GetBool(e, "IsRegex"), GetBool(e, "IgnoreCase")));
                }
            }
            return list;
        }
        catch
        {
            return null;
        }
    }

    static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    static bool GetBool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
