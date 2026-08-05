using System.Text;

namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// ゆっくり (AquesTalk) 記法のアクセント指定を AITalk の読み記号 (AI-Kana) へ橋渡しするユーティリティ。
///
/// AquesTalk 完全準拠ではなく、**アクセント核の指定のみ**に対応する:
///   モーラの直後に「'」(アポストロフィ) を置くと、そのモーラからピッチが下がる
///   例: ハ'シ (橋) / ハシ' (箸)
///
/// 変換は「かなモーラ列の位置合わせ」で行う:
///   - 記号を除いたかなテキストを AITalk の TextToKana に渡し、
///   - 返ってきた AI-Kana のモーラ列と入力のかなモーラ列を突き合わせて
///   - 指定モーラの直後にアクセント核 (^) を挿入する。
/// モーラ数が一致しない場合は無変換 (AI-Kana のまま) を返す。
/// </summary>
public static class AquesTalkKana
{
    /// <summary>アクセント核マーク (対象モーラの直後に置く)。</summary>
    public const char AccentMark = '\'';

    /// <summary>直前のモーラに連結する小書き文字 (ッ は促音なので独立モーラ)。</summary>
    static readonly HashSet<char> AttachKana = new("ァィゥェォャュョヮ");

    static bool IsKana(char c)
        => (c >= '\u30A1' && c <= '\u30F6') || (c >= '\u3041' && c <= '\u3096');

    /// <summary>かなモーラ 1 つの位置 (文字列内オフセット)。</summary>
    public readonly record struct Mora(int Start, int Length);

    /// <summary>
    /// 文字列からかなモーラ列を切り出す。
    /// 記号 (アクセントマーク・句読点・AI-Kana の制御記号) は無視され、
    /// 小書き (ャュョ等) と長音 (ー) は直前のモーラに連結される。促音 (ッ) は独立モーラ。
    /// </summary>
    public static List<Mora> SplitMorae(string text)
    {
        var morae = new List<Mora>();
        int i = 0;
        while (i < text.Length)
        {
            if (!IsKana(text[i])) { i++; continue; }
            int start = i;
            i++;
            while (i < text.Length && (AttachKana.Contains(text[i]) || text[i] == '\u30FC'))
                i++;
            morae.Add(new Mora(start, i - start));
        }
        return morae;
    }

    /// <summary>アクセントマークを取り除いたテキストを返す。</summary>
    public static string StripAccentMarks(string text)
        => text.Replace(AccentMark.ToString(), string.Empty);

    /// <summary>
    /// アクセントマーク付きテキストの指定を AI-Kana へ反映する。
    /// マークを含むアクセント句 ($ / | / ( 区切り) の既定アクセント (^) は除去され、
    /// 指定モーラの直後に ^ が挿入される。モーラ数が一致しない場合は元の AI-Kana を返す。
    /// </summary>
    public static string ApplyAccentMarks(string source, string aiKana)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(aiKana))
            return aiKana;

        var sourceMorae = SplitMorae(source);
        var aiMorae = SplitMorae(aiKana);
        if (sourceMorae.Count == 0 || aiMorae.Count != sourceMorae.Count)
            return aiKana;

        // マーク位置 (ソースのモーラインデックス)
        var marked = new List<int>();
        for (int i = 0; i < sourceMorae.Count; i++)
        {
            var mora = sourceMorae[i];
            int after = mora.Start + mora.Length;
            if (after < source.Length && source[after] == AccentMark)
                marked.Add(i);
        }
        if (marked.Count == 0)
            return aiKana;

        // アクセント句の境界 (AI-Kana 上: $ / | / ( )
        var segStarts = new List<int> { 0 };
        for (int i = 0; i < aiKana.Length; i++)
        {
            if (aiKana[i] is '$' or '|' or '(')
                segStarts.Add(i);
        }
        segStarts.Add(aiKana.Length);

        static int SegmentOf(int charIndex, List<int> segStarts)
        {
            int seg = 0;
            for (int s = 0; s + 1 < segStarts.Count && segStarts[s + 1] <= charIndex; s++)
                seg = s + 1;
            return seg;
        }

        // 各モーラが属する句
        var segOfMora = new int[aiMorae.Count];
        for (int gi = 0; gi < aiMorae.Count; gi++)
            segOfMora[gi] = SegmentOf(aiMorae[gi].Start, segStarts);

        var stripSegments = new HashSet<int>(marked.Select(m => segOfMora[m]));
        var insertAt = new HashSet<int>(marked.Select(m => aiMorae[m].Start + aiMorae[m].Length));

        var sb = new StringBuilder(aiKana.Length + marked.Count);
        for (int i = 0; i < aiKana.Length; i++)
        {
            if (insertAt.Contains(i))
                sb.Append('^');
            char c = aiKana[i];
            if (c == '^')
            {
                // マークを含む句の既定アクセントは除去する
                if (stripSegments.Contains(SegmentOf(i, segStarts)))
                    continue;
            }
            sb.Append(c);
        }
        if (insertAt.Contains(aiKana.Length))
            sb.Append('^'); // 文末モーラへのアクセント指定 (例: ハシ')
        return sb.ToString();
    }

    /// <summary>
    /// AI-Kana を編集用のかなテキストへ変換する (ConvertKanjiToYomiAsync 用)。
    /// - アクセント核 (^) → 「'」
    /// - 句読点ポーズ ($N_N) → 「、」
    /// - JEITA 制御記号 ((Irq MARK=...) / &lt;S&gt; / &lt;H&gt;)・無声化 (!)・短ポーズ (|) は除去
    ///   (無声化やポーズは TextToKana が再生成するため表示しない)
    /// </summary>
    public static string ToEditableKana(string aiKana)
    {
        if (string.IsNullOrEmpty(aiKana))
            return aiKana;

        var morae = SplitMorae(aiKana);
        var sb = new StringBuilder(aiKana.Length);
        int mi = 0;
        for (int i = 0; i < aiKana.Length; i++)
        {
            if (mi < morae.Count && morae[mi].Start == i)
            {
                var mora = morae[mi++];
                sb.Append(aiKana, mora.Start, mora.Length);
                int end = mora.Start + mora.Length;
                if (end < aiKana.Length && aiKana[end] == '^')
                {
                    sb.Append(AccentMark);
                    i = end;
                }
                else
                {
                    i = end - 1;
                }
                continue;
            }

            char c = aiKana[i];
            switch (c)
            {
                case '$':
                    sb.Append('、');
                    break;
                case '^':
                case '!':
                case '|':
                    break; // 既定アクセント・無声化・短ポーズは表示しない
                case '(':
                case '<':
                    // (Irq MARK=...) / <S> <H> を読み飛ばす
                    char close = c == '(' ? ')' : '>';
                    int end = aiKana.IndexOf(close, i + 1);
                    if (end > 0) i = end;
                    break;
                default:
                    if (char.IsDigit(c) || c == '_' || c == '=')
                        break; // $ に続く長さ表記や MARK= の残り
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
