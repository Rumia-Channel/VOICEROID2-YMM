using System.Windows;

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

    void OkButton_Click(object sender, RoutedEventArgs e)
    {
        vm.Commit();
        committed = true;
        DialogResult = true;
        Close();
    }

    void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
