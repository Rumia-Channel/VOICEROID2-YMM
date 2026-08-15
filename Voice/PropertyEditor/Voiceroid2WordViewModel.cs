using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Voiceroid2Ymm.Voice.AITalk;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// アクセントエディタの単語 (アクセント句) 1 つ分。
/// アクセント核 (モーラインデックス) を保持し、核までのモーラが高アクセントになる。
/// </summary>
internal sealed class Voiceroid2WordViewModel : INotifyPropertyChanged
{
    readonly AccentEditKana.AccentWord source;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>ヘッダー表示 (読み)。</summary>
    public string Surface { get; }

    public ObservableCollection<Voiceroid2MoraViewModel> Moras { get; }

    /// <summary>句読点・空白のみの単語か。</summary>
    public bool IsPunctuationWord { get; }

    /// <summary>アクセント核のモーラインデックス (-1 = 指定なし → 合成時にエンジン既定)。</summary>
    public int AccentPosition { get; private set; }

    /// <summary>ユーザーがアクセントを指定しているか。</summary>
    public bool HasUserAccent => AccentPosition >= 0;

    /// <summary>
    /// エンジン既定のアクセント核位置 (読み込んだ時点の値)。
    /// アクセントを解除 (-1) したときに、エンジンが実際に下げる位置を破線マーカーで示すために使う。
    /// </summary>
    public int DefaultAccentPosition { get; }

    /// <summary>表示・編集に使う有効な核位置 (ユーザー指定があればそれ、なければエンジン既定)。</summary>
    public int EffectiveAccentPosition => AccentPosition >= 0 ? AccentPosition : DefaultAccentPosition;

    /// <summary>アクセント核の位置で句を分割できるか (核が句の内部にある場合のみ)。</summary>
    public bool CanSplitAccent
    {
        get
        {
            int k = EffectiveAccentPosition;
            return k > 0 && k < Moras.Count;
        }
    }

    bool isEditingReading;
    string? readingText;

    /// <summary>読み編集モード中か (ヘッダー帯クリックで入る)。</summary>
    public bool IsEditingReading
    {
        get => isEditingReading;
        set
        {
            if (isEditingReading == value) return;
            isEditingReading = value;
            Notify();
        }
    }

    /// <summary>読み編集欄のテキスト。未編集なら現在の読みを返す。</summary>
    public string ReadingText
    {
        get => readingText ?? EditableReading;
        set
        {
            if (readingText == value) return;
            readingText = value;
            Notify();
        }
    }

    /// <summary>現在の読み (全モーラのかな連結、アクセントマークなし)。</summary>
    public string EditableReading
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var mora in Moras)
                sb.Append(mora.Kana == " " ? string.Empty : mora.Kana);
            return sb.ToString();
        }
    }

    /// <summary>読み編集モードへ入る (ヘッダー帯クリック時)。</summary>
    public void BeginReadingEdit()
    {
        if (IsPunctuationWord || IsEditingReading) return;
        readingText = EditableReading;
        IsEditingReading = true;
    }

    /// <summary>読み編集をキャンセルして現在の読みへ戻す (Esc 時)。</summary>
    public void CancelReadingEdit()
    {
        readingText = null;
        IsEditingReading = false;
    }

    public Voiceroid2WordViewModel(AccentEditKana.AccentWord source)
    {
        this.source = source;
        Surface = string.IsNullOrWhiteSpace(source.Text) ? " " : source.Text;
        IsPunctuationWord = source.IsPunctuation;
        AccentPosition = source.AccentPosition;
        DefaultAccentPosition = source.AccentPosition;

        Moras = new ObservableCollection<Voiceroid2MoraViewModel>();
        for (int i = 0; i < source.Moras.Count; i++)
        {
            Moras.Add(new Voiceroid2MoraViewModel(this, i, source.Moras[i], editable: true));
        }
    }

    /// <summary>アクセント核をモーラ境界へ直接移動する (核マーカーの左右ドラッグ用)。</summary>
    public void MoveNucleusToBoundary(int boundary)
    {
        if (boundary >= Moras.Count)
        {
            // 最後のモーラより後ろ = 句内に下がりなし → エンジン既定へ
            ClearAccent();
            return;
        }
        SetAccent(boundary);
    }

    /// <summary>アクセント核を指定位置へ移動する。</summary>
    public void SetAccent(int index)
    {
        if (index < 0 || index >= Moras.Count) return;
        if (!Moras[index].IsAccentEditable) return;
        if (AccentPosition == index) return;
        AccentPosition = index;
        NotifyAccents();
    }

    /// <summary>アクセント指定を解除する (合成時にエンジン既定が使われる)。</summary>
    public void ClearAccent()
    {
        if (AccentPosition < 0) return;
        AccentPosition = -1;
        NotifyAccents();
    }

    /// <summary>クリック位置のアクセントをトグルする (現在の核なら解除、それ以外は移動)。</summary>
    public void ToggleAccent(int index)
    {
        if (index < 0 || index >= Moras.Count || !Moras[index].IsAccentEditable) return;
        if (AccentPosition == index) ClearAccent();
        else SetAccent(index);
    }

    void NotifyAccents()
    {
        foreach (var mora in Moras)
            mora.NotifyAccentChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccentPosition)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUserAccent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSplitAccent)));
    }

    void Notify([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
