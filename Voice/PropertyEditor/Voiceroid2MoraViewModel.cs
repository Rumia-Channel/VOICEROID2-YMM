using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// アクセントエディタのモーラ 1 つ分 (VOICEPEAK-plus の
/// VoicePeakPronounceMoraViewModel に相当。アクセントのみ対応)。
/// </summary>
internal sealed class Voiceroid2MoraViewModel : INotifyPropertyChanged
{
    readonly Voiceroid2WordViewModel word;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>単語内のモーラインデックス。</summary>
    public int Index { get; }

    /// <summary>表示用のかな (空白モーラは「 」)。</summary>
    public string Kana { get; }

    /// <summary>アクセント編集可能 (かなモーラ) か。</summary>
    public bool IsAccentEditable { get; }

    /// <summary>句読点などの特殊モーラか。</summary>
    public bool IsSpecialMora => !IsAccentEditable;

    /// <summary>このモーラが高アクセントか (アクセント核の位置以下)。</summary>
    public bool IsAccentHigh => IsAccentEditable && word.AccentPosition >= Index;

    /// <summary>高/低の表示ラベル (モーラの下に表示する)。</summary>
    public string AccentLabel => IsAccentEditable ? (IsAccentHigh ? "高" : "低") : "－";

    public Voiceroid2MoraViewModel(Voiceroid2WordViewModel word, int index, string kana, bool editable)
    {
        this.word = word;
        Index = index;
        Kana = string.IsNullOrWhiteSpace(kana) ? " " : kana;
        IsAccentEditable = editable;
    }

    /// <summary>アクセント核のトグル (エディタのクリックから呼ばれる)。</summary>
    public void ToggleAccent() => word.ToggleAccent(Index);

    /// <summary>
    /// このモーラを「高」にする (ドラッグで上へ)。
    /// アクセント核をこのモーラの直後に移動する。最後のモーラなら句内に下がりなし
    /// (エンジン既定) へ戻す。
    /// </summary>
    public void SetAccentHigh()
    {
        int pos = word.AccentPosition;
        if (pos > Index) return; // すでに高

        if (Index + 1 < word.Moras.Count)
            word.SetAccent(Index + 1);
        else
            word.ClearAccent();
    }

    /// <summary>このモーラを「低」にする (ドラッグで下へ)。アクセント核をこのモーラへ移動する。</summary>
    public void SetAccentLow()
    {
        int pos = word.AccentPosition;
        if (pos <= Index) return; // すでに低 (未指定時は表示が低のため無操作)

        word.SetAccent(Index);
    }

    /// <summary>単語のアクセント位置変更を反映する (word から呼ばれる)。</summary>
    public void NotifyAccentChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccentHigh)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccentLabel)));
    }
}
