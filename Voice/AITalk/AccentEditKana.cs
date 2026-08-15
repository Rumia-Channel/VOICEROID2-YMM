using System.Text;

namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// アクセントエディタのデータモデル: 編集用かな (アクセントマーク「'」付き) を
/// 単語 (アクセント句) とモーラ・アクセント位置へ分解する。
///
/// 編集用かなの例: 「コ'ンニチワ、ユ'ックリ」
///   - かな連続 = 1 単語 (アクセント句)
///   - 句読点 (、。？！…) と空白は独立した単語になる
///   - モーラ直後の「'」がアクセント核 (そのモーラからピッチが下がる)
/// </summary>
public static class AccentEditKana
{
    /// <summary>1 単語 (アクセント句) の編集データ。</summary>
    public sealed record AccentWord(string Text, IReadOnlyList<string> Moras, int AccentPosition)
    {
        /// <summary>句読点・空白のみの単語か。</summary>
        public bool IsPunctuation => Moras.Count == 0;
    }

    static readonly HashSet<char> Punctuation = new("、。？！…・|");

    /// <summary>
    /// 編集用かなを単語列へ分解する。
    /// アクセントマークが複数ある場合は最初の位置を採用する。
    /// </summary>
    public static List<AccentWord> Parse(string editableKana)
    {
        var words = new List<AccentWord>();
        if (string.IsNullOrEmpty(editableKana)) return words;

        int i = 0;
        while (i < editableKana.Length)
        {
            char c = editableKana[i];
            if (Punctuation.Contains(c) || char.IsWhiteSpace(c))
            {
                // 句読点・空白の連続は 1 つの単語にまとめる
                int start = i;
                while (i < editableKana.Length
                       && (Punctuation.Contains(editableKana[i]) || char.IsWhiteSpace(editableKana[i])))
                {
                    i++;
                }
                words.Add(new AccentWord(
                    editableKana.Substring(start, i - start),
                    Array.Empty<string>(),
                    -1));
                continue;
            }

            // かな連続 (アクセントマークを含む)
            int kanaStart = i;
            while (i < editableKana.Length
                   && !Punctuation.Contains(editableKana[i])
                   && !char.IsWhiteSpace(editableKana[i]))
            {
                i++;
            }
            string segment = editableKana.Substring(kanaStart, i - kanaStart);

            var morae = AquesTalkKana.SplitMorae(segment);
            int accent = -1;
            for (int mi = 0; mi < morae.Count; mi++)
            {
                var mora = morae[mi];
                int after = mora.Start + mora.Length;
                if (after < segment.Length && segment[after] == AquesTalkKana.AccentMark)
                {
                    accent = mi;
                    break;
                }
            }

            // マークを除いたかなをモーラ文字列として保持する
            var kanaChars = new List<string>(morae.Count);
            foreach (var mora in morae)
                kanaChars.Add(segment.Substring(mora.Start, mora.Length));

            words.Add(new AccentWord(string.Concat(kanaChars), kanaChars, accent));
        }
        return words;
    }

    /// <summary>
    /// 単語列から編集用かなを再構築する (アクセント位置は現在値)。
    /// 句読点単語はそのまま、かな単語はモーラ連結 + アクセント核のモーラ直後に「'」。
    /// </summary>
    public static string Build(IEnumerable<AccentWord> words)
    {
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.IsPunctuation)
            {
                sb.Append(word.Text);
                continue;
            }

            int position = word.AccentPosition;
            for (int i = 0; i < word.Moras.Count; i++)
            {
                sb.Append(word.Moras[i]);
                if (i == position)
                    sb.Append(AquesTalkKana.AccentMark);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// アクセント核の位置で単語を 2 つのアクセント句へ分割した編集用かなを返す。
    /// 句読点なしの単語かな (アクセントマークなし) と核位置 (1..count-1) を受け取り、
    /// 「前半 (核の手前まで) + ' + | + 後半の先頭 + ' + 後半の残り」を組み立てる。
    /// 区切りは無音の句境界 (|) で、ポーズ (、) は挿入しない。
    /// 前半は核を末尾に、後半は核を先頭に置くため、分割前の高低の流れが保たれる。
    /// 例: ハシ (核 1) → ハ'|シ'
    /// </summary>
    public static string? SplitWordAtNucleus(string wordKana, int nucleus)
    {
        if (string.IsNullOrEmpty(wordKana) || nucleus <= 0) return null;

        var morae = AquesTalkKana.SplitMorae(wordKana);
        if (nucleus >= morae.Count) return null;

        var sb = new StringBuilder();
        for (int i = 0; i < nucleus; i++)
            sb.Append(wordKana.Substring(morae[i].Start, morae[i].Length));
        sb.Append(AquesTalkKana.AccentMark); // 前半: 核を末尾に (ここまで高)

        sb.Append('|'); // 無音のアクセント句境界 (ポーズではない)

        sb.Append(wordKana.Substring(morae[nucleus].Start, morae[nucleus].Length));
        sb.Append(AquesTalkKana.AccentMark); // 後半: 核を先頭に (ここから低)

        for (int i = nucleus + 1; i < morae.Count; i++)
            sb.Append(wordKana.Substring(morae[i].Start, morae[i].Length));

        return sb.ToString();
    }
}
