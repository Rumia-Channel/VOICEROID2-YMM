using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// 読み仮名辞書の 1 エントリ。表記 (Surface) → 読み (Reading, カタカナ) の対応を表す。
/// VoicePeak-plus の ReadingEntry と同じ設計。
/// </summary>
public class ReadingEntry : INotifyPropertyChanged
{
    string surface = string.Empty;
    string reading = string.Empty;
    bool enabled = true;

    /// <summary>本文中の表記 (例: "人名", "VOICEROID2")。</summary>
    public string Surface
    {
        get => surface;
        set { if (surface != value) { surface = value ?? string.Empty; OnPropertyChanged(); } }
    }

    /// <summary>置換後の読み (カタカナ。例: "ヒトノナ", "ボイスロイドツー")。</summary>
    public string Reading
    {
        get => reading;
        set { if (reading != value) { reading = value ?? string.Empty; OnPropertyChanged(); } }
    }

    /// <summary>false にするとこのエントリは適用対象外になる。</summary>
    public bool Enabled
    {
        get => enabled;
        set { if (enabled != value) { enabled = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Surface)
                        && !string.IsNullOrWhiteSpace(Reading);

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 読み仮名辞書を適用したテキストを生成するユーティリティ。
/// 表記 (Surface) を読み (Reading) に**文字列置換**する (VoicePeak-plus の ReadingApplier と同方針)。
///
/// 注意点:
/// <list type="bullet">
///   <item>YMM4 上の表示テキストは「表記」のまま、合成される音声は「読み」になる。</item>
///   <item>置換はリテラルに行うため、表記と読みの文字種や長さが違っても問題ない。</item>
///   <item>置換は最長一致を先に処理する (例: 「USB」と「US」が両方登録されている場合、USB を優先)。</item>
///   <item>空 / 空白のみのエントリは無視される。</item>
/// </list>
/// </summary>
public static class ReadingApplier
{
    /// <summary>
    /// 辞書エントリを <paramref name="text"/> に適用する。
    /// 単一パスで最長一致のマッチを検出し、置換結果が別のエントリに
    /// 再置換される連鎖置換を防ぐ。
    /// </summary>
    public static string Apply(string text, IEnumerable<ReadingEntry> entries)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sorted = entries
            .Where(e => e.Enabled && e.IsValid)
            .OrderByDescending(e => e.Surface.Length)
            .ToList();
        if (sorted.Count == 0) return text;

        // 原文に対して全エントリのマッチ位置を収集する（最長一致優先）。
        var matches = new List<(int Start, int Length, string Replacement)>();
        var occupied = new bool[text.Length];

        foreach (var entry in sorted)
        {
            int searchFrom = 0;
            while (searchFrom <= text.Length - entry.Surface.Length)
            {
                int index = text.IndexOf(entry.Surface, searchFrom, StringComparison.Ordinal);
                if (index < 0) break;

                bool overlaps = false;
                for (int i = index; i < index + entry.Surface.Length; i++)
                {
                    if (occupied[i]) { overlaps = true; break; }
                }

                if (!overlaps)
                {
                    matches.Add((index, entry.Surface.Length, entry.Reading));
                    for (int i = index; i < index + entry.Surface.Length; i++)
                        occupied[i] = true;
                }

                searchFrom = index + 1;
            }
        }

        if (matches.Count == 0) return text;

        // 末尾から置換するとインデックスがずれない。
        matches.Sort((a, b) => b.Start.CompareTo(a.Start));
        var result = text;
        foreach (var (start, length, replacement) in matches)
        {
            result = string.Concat(result.AsSpan(0, start), replacement, result.AsSpan(start + length));
        }
        return result;
    }
}
