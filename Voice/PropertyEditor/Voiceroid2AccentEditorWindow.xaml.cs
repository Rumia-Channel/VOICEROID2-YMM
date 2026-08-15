using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// VOICEROID2 発音編集ウィンドウ (アクセント編集のみ)。
/// 単語カード (読み + アクセント折れ線) を横に並べ、モーラのクリックで
/// アクセント核を変更する。OK で適用、キャンセルで破棄。
/// </summary>
public partial class Voiceroid2AccentEditorWindow : Window
{
    readonly Voiceroid2AccentEditorViewModel vm;
    readonly Action? beginEditAction;
    readonly Action? endEditAction;
    bool committed;
    bool disposed;

    internal Voiceroid2AccentEditorWindow(
        Voiceroid2AccentEditorViewModel vm,
        Action? beginEdit = null,
        Action? endEdit = null)
    {
        this.vm = vm;
        beginEditAction = beginEdit;
        endEditAction = endEdit;

        InitializeComponent();
        DataContext = vm;

        Closed += OnClosed;
    }

    async void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // 入力直後でまだ反映されていない読み編集を先に適用する
        // (各適用は例外を握りつぶすため、この待機で例外は発生しない)
        try
        {
            await vm.FlushPendingReadingAppliesAsync();
        }
        catch
        {
            // 適用失敗時はエディタの状態のまま確定させず、キャンセル扱いにしない
        }

        vm.Commit();
        committed = true;
        Close();
    }

    /// <summary>ヘッダー帯をクリックして読み編集モードに入る (VOICEPEAK 準拠)。</summary>
    void HeaderBand_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Voiceroid2WordViewModel wv)
            return;
        if (wv.IsPunctuationWord)
            return;

        wv.BeginReadingEdit();
        e.Handled = true;
    }

    /// <summary>アクセント核の位置で単語を 2 つのアクセント句へ分割する。</summary>
    void SplitPhraseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Voiceroid2WordViewModel wv)
            return;
        if (DataContext is Voiceroid2AccentEditorViewModel evm)
            evm.SplitWordAtAccent(wv);
    }

    /// <summary>読み入力欄が表示されたらフォーカスして全選択する。</summary>
    void ReadingTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Visibility != Visibility.Visible)
            return;

        tb.Dispatcher.BeginInvoke(new Action(() =>
        {
            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Loaded);
    }

    void ReadingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            CommitReading(tb);
    }

    void ReadingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (e.Key == Key.Enter)
        {
            CommitReading(tb);
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (tb.DataContext is Voiceroid2WordViewModel wv)
                wv.CancelReadingEdit();
            e.Handled = true;
        }
    }

    /// <summary>読み編集を確定し、読みが変わっていればエンジンへ適用する。</summary>
    void CommitReading(TextBox tb)
    {
        if (tb.DataContext is not Voiceroid2WordViewModel wv || !wv.IsEditingReading)
            return;

        wv.IsEditingReading = false;

        string reading = wv.ReadingText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reading))
            return;
        if (reading == wv.EditableReading)
            return;

        if (DataContext is Voiceroid2AccentEditorViewModel evm)
            _ = evm.ApplyWordReadingAsync(wv);
    }

    void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // 非モーダル表示 (Show) のため DialogResult は使えない。committed フラグで
        // 確定/破棄を判別する (Close で OnClosed → Revert)。
        Close();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        if (disposed) return;
        disposed = true;

        if (!committed)
            vm.Revert();
        vm.Dispose();

        endEditAction?.Invoke();
    }
}
