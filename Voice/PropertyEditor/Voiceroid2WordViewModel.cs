using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public Voiceroid2WordViewModel(AccentEditKana.AccentWord source)
    {
        this.source = source;
        Surface = string.IsNullOrWhiteSpace(source.Text) ? " " : source.Text;
        IsPunctuationWord = source.IsPunctuation;
        AccentPosition = source.AccentPosition;

        Moras = new ObservableCollection<Voiceroid2MoraViewModel>();
        for (int i = 0; i < source.Moras.Count; i++)
        {
            Moras.Add(new Voiceroid2MoraViewModel(this, i, source.Moras[i], editable: true));
        }
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
    }
}
