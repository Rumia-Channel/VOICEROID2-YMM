using System.Windows;
using System.Windows.Controls;
using Voiceroid2Ymm.Voice.AITalk;
using YukkuriMovieMaker.Commons;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// 音声アイテムの発音 (アクセント) を編集する YMM4 カスタムプロパティエディタ。
/// 編集 UI は <see cref="Voiceroid2AccentEditorWindow"/> として別ウィンドウで表示される。
/// </summary>
public partial class Voiceroid2PronounceEditor : UserControl, IPropertyEditorControl2
{
    IEditorInfo? info;

    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;

    /// <summary>プレビュー再生用の合成パラメータ。</summary>
    internal Voiceroid2VoiceParameter? Parameter { get; set; }

    Voiceroid2AccentEditorWindow? openedWindow;
    bool editOpened;
    bool isOpeningWindow;

    public Voiceroid2VoicePronounce? Pronounce
    {
        get => (Voiceroid2VoicePronounce?)GetValue(PronounceProperty);
        set => SetValue(PronounceProperty, value);
    }

    public static readonly DependencyProperty PronounceProperty =
        DependencyProperty.Register(
            nameof(Pronounce),
            typeof(Voiceroid2VoicePronounce),
            typeof(Voiceroid2PronounceEditor),
            new PropertyMetadata(null, OnPronounceChanged));

    public Voiceroid2PronounceEditor()
    {
        InitializeComponent();
        UpdateOpenButtonEnabled();
    }

    public void SetEditorInfo(IEditorInfo? info)
    {
        this.info = info;
    }

    static void OnPronounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Voiceroid2PronounceEditor editor)
            editor.UpdateViewModel();
    }

    void UpdateViewModel()
    {
        CloseOpenedWindow();
        UpdateOpenButtonEnabled();
    }

    void CloseOpenedWindow()
    {
        if (openedWindow is null) return;

        var w = openedWindow;
        openedWindow = null;

        try { w.Close(); }
        catch { /* ignore */ }
    }

    void UpdateOpenButtonEnabled()
        => OpenWindowButton.IsEnabled = Pronounce is not null;

    internal async void OpenWindow_Click(object sender, RoutedEventArgs e)
    {
        var pronounce = Pronounce;
        if (pronounce is null) return;
        if (isOpeningWindow) return;

        if (openedWindow is { IsLoaded: true })
        {
            openedWindow.Activate();
            return;
        }

        isOpeningWindow = true;
        OpenWindowButton.IsEnabled = false;
        try
        {
            var snapshotPronounce = pronounce;
            var snapshotParameter = Parameter;

            // 編集の基準となる読み (編集用かな) を用意する
            // (手動編集が無ければエンジンで再生成する)
            string initialKana;
            try
            {
                initialKana = await GetInitialKanaAsync(pronounce, Parameter).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"発音編集を開けませんでした。\n{ex.Message}",
                    "VOICEROID2 発音編集",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 非同期処理中に編集対象が変わっていないか再確認する。
            if (!ReferenceEquals(snapshotPronounce, Pronounce)
                || !ReferenceEquals(snapshotParameter, Parameter))
            {
                return;
            }

            var vm = new Voiceroid2AccentEditorViewModel(pronounce, Parameter, initialKana);

            Voiceroid2AccentEditorWindow window;
            try
            {
                var owner = Window.GetWindow(this);
                window = new Voiceroid2AccentEditorWindow(
                    vm,
                    () => BeginEdit?.Invoke(this, EventArgs.Empty),
                    () => EndEdit?.Invoke(this, EventArgs.Empty));
                if (owner is not null)
                    window.Owner = owner;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"発音編集ウィンドウを作成できませんでした。\n{ex.Message}",
                    "VOICEROID2 発音編集",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                vm.Dispose();
                return;
            }

            // ウィンドウ構築が成功してから BeginEdit を投げる。
            if (!editOpened)
            {
                editOpened = true;
                BeginEdit?.Invoke(this, EventArgs.Empty);
            }

            window.Closed += (_, _) =>
            {
                openedWindow = null;
                if (editOpened)
                {
                    editOpened = false;
                    EndEdit?.Invoke(this, EventArgs.Empty);
                }
                UpdateOpenButtonEnabled();
            };

            openedWindow = window;
            window.Show();
        }
        finally
        {
            isOpeningWindow = false;
        }
    }

    /// <summary>
    /// 編集の基準となる編集用かなを取得する。
    /// 手動編集済みならその内容、未編集なら合成パイプラインで生成する。
    /// </summary>
    static async Task<string> GetInitialKanaAsync(Voiceroid2VoicePronounce pronounce, Voiceroid2VoiceParameter? parameter)
    {
        if (pronounce.IsManualEdit && !string.IsNullOrWhiteSpace(pronounce.EditKana))
            return pronounce.EditKana;

        string sourceText = pronounce.SourceText;
        if (string.IsNullOrWhiteSpace(sourceText))
            return string.Empty;

        string voiceName = parameter?.VoiceName ?? pronounce.NarratorName;
        if (string.IsNullOrWhiteSpace(voiceName))
            return string.Empty;

        // 合成時と同じパイプライン: 記号除去 → TextToKana → アクセント反映 → 編集用かな
        await Voiceroid2EngineGate.Semaphore.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                AITalkEngine.EnsureOpened(
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvInstallDir),
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvUserDir),
                    voiceName);

                string plain = AquesTalkKana.StripAccentMarks(sourceText);
                string aiKana = AITalkEngine.DecodeAnsiText(AITalkEngine.TextToKana(plain));
                aiKana = AquesTalkKana.ApplyAccentMarks(sourceText, aiKana);
                return AquesTalkKana.ToEditableKana(aiKana);
            });
        }
        finally
        {
            Voiceroid2EngineGate.Semaphore.Release();
        }
    }
}
