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

    static readonly HashSet<char> Punctuation = new("、。？！…・");

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
}
