using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows.Input;
using Voiceroid2Ymm.Voice.AITalk;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// アクセント編集ウィンドウのビューモデル。
/// 編集用かな (アクセントマーク付き) を単語・モーラへ分解して表示し、
/// 編集結果を編集用かなへ再構築する。
/// </summary>
internal sealed class Voiceroid2AccentEditorViewModel : IDisposable
{
    readonly Voiceroid2VoicePronounce pronounce;
    readonly Voiceroid2VoiceParameter? parameter;
    readonly string initialKana;
    readonly string originalEditKana;
    readonly bool originalManual;

    SoundPlayer? player;

    public ObservableCollection<Voiceroid2WordViewModel> Words { get; }

    public ICommand ResetCommand { get; }
    public ICommand PlayCommand { get; }

    /// <summary>編集中 (未確定) の編集用かな。</summary>
    public string CurrentKana
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var word in Words)
            {
                if (word.IsPunctuationWord)
                {
                    sb.Append(word.Surface);
                    continue;
                }

                int position = word.AccentPosition;
                for (int i = 0; i < word.Moras.Count; i++)
                {
                    sb.Append(word.Moras[i].Kana == " " ? string.Empty : word.Moras[i].Kana);
                    if (i == position)
                        sb.Append(AquesTalkKana.AccentMark);
                }
            }
            return sb.ToString();
        }
    }

    public Voiceroid2AccentEditorViewModel(
        Voiceroid2VoicePronounce pronounce,
        Voiceroid2VoiceParameter? parameter,
        string initialKana)
    {
        this.pronounce = pronounce;
        this.parameter = parameter;
        this.initialKana = initialKana;
        originalEditKana = pronounce.EditKana;
        originalManual = pronounce.IsManualEdit;

        Words = new ObservableCollection<Voiceroid2WordViewModel>(
            AccentEditKana.Parse(initialKana).Select(w => new Voiceroid2WordViewModel(w)));

        ResetCommand = new RelayCommand(ResetToDefault);
        PlayCommand = new RelayCommand(async () => await PlayPreviewAsync());
    }

    /// <summary>編集前の状態へ戻す (キャンセル時)。変化が無ければ何もしない。</summary>
    public void Revert()
    {
        StopPlayback();
        if (pronounce.EditKana != originalEditKana || pronounce.IsManualEdit != originalManual)
        {
            pronounce.SetEditKana(pronounce.SourceText, pronounce.NarratorName, originalEditKana, originalManual);
        }
    }

    /// <summary>編集結果を確定する (OK 時)。</summary>
    public void Commit()
    {
        StopPlayback();
        pronounce.SetEditKana(pronounce.SourceText, pronounce.NarratorName, CurrentKana, manual: true);
    }

    /// <summary>全単語のアクセントを編集中の最初の状態 (エンジン既定) へ戻す。</summary>
    public void ResetToDefault()
    {
        var restored = AccentEditKana.Parse(initialKana);
        Words.Clear();
        foreach (var word in restored)
            Words.Add(new Voiceroid2WordViewModel(word));
    }

    /// <summary>編集中の読みで音声をプレビュー再生する。</summary>
    async Task PlayPreviewAsync()
    {
        string kana = CurrentKana;
        string plain = AquesTalkKana.StripAccentMarks(kana);
        string voiceName = parameter?.VoiceName ?? pronounce.NarratorName;
        if (string.IsNullOrWhiteSpace(voiceName))
            return;

        string wavPath = Path.Combine(Path.GetTempPath(), "vo2_preview.wav");
        try
        {
            await Voiceroid2EngineGate.Semaphore.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    AITalkEngine.EnsureOpened(
                        AITalkInstallation.EnvValue(AITalkInstallation.EnvInstallDir),
                        AITalkInstallation.EnvValue(AITalkInstallation.EnvUserDir),
                        voiceName);

                    string aiKana = AITalkEngine.DecodeAnsiText(AITalkEngine.TextToKana(plain));
                    aiKana = AquesTalkKana.ApplyAccentMarks(kana, aiKana);
                    var pcm = AITalkEngine.KanaToSpeech(
                        AITalkEngine.EncodeAnsiText(aiKana),
                        ToSpeakerParams());
                    WavFile.WritePcm16Mono(wavPath, pcm);
                });
            }
            finally
            {
                Voiceroid2EngineGate.Semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher
                && dispatcher.CheckAccess())
            {
                System.Windows.MessageBox.Show(
                    System.Windows.Window.GetWindow(System.Windows.Application.Current.MainWindow),
                    $"プレビュー再生に失敗しました。\n{ex.Message}",
                    "VOICEROID2 発音編集",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            return;
        }

        StopPlayback();
        player = new SoundPlayer(wavPath);
        player.Play();
    }

    AITalkSpeakerParams ToSpeakerParams()
    {
        if (parameter is null)
            return new AITalkSpeakerParams(Volume: 1.0f, Speed: 1.0f, Pitch: 1.0f, Range: 1.0f, PauseSentence: 500);
        return new AITalkSpeakerParams(
            Volume: (float)parameter.Volume,
            Speed: (float)parameter.Speed,
            Pitch: (float)parameter.Pitch,
            Range: (float)parameter.Range,
            PauseSentence: (int)parameter.PauseSentence);
    }

    public void StopPlayback()
    {
        player?.Stop();
        player?.Dispose();
        player = null;
    }

    public void Dispose()
    {
        StopPlayback();
    }

    /// <summary>簡易 ICommand。</summary>
    sealed class RelayCommand : ICommand
    {
        readonly Action execute;
        public RelayCommand(Action execute) => this.execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
