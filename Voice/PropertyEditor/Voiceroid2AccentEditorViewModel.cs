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

    readonly List<Task> pendingReadingApplies = new();

    /// <summary>
    /// 単語の読み (よみがな) をエンジンで正規化し、モーラ列・アクセントを再取得して反映する。
    /// 読みが変わらない場合や空の場合は何もしない。エラーはここで処理し、タスクは例外を出さない。
    /// </summary>
    public async Task ApplyWordReadingAsync(Voiceroid2WordViewModel wordVm)
    {
        if (wordVm is null) return;

        var task = ApplyWordReadingCoreAsync(wordVm);
        pendingReadingApplies.Add(task);
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher
                && dispatcher.CheckAccess())
            {
                System.Windows.MessageBox.Show(
                    System.Windows.Window.GetWindow(System.Windows.Application.Current.MainWindow),
                    $"読みを反映できませんでした。\n{ex.Message}",
                    "VOICEROID2 発音編集",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        finally
        {
            pendingReadingApplies.Remove(task);
        }
    }

    /// <summary>進行中の読み反映処理が終わるまで待つ (OK / プレビューの直前用)。</summary>
    public Task FlushPendingReadingAppliesAsync()
    {
        var pending = pendingReadingApplies.ToArray();
        return pending.Length == 0 ? Task.CompletedTask : Task.WhenAll(pending);
    }

    /// <summary>
    /// 単語をアクセント核の位置で 2 つのアクセント句へ分割する (句読点ポーズ「、」を挿入)。
    /// 分割後は各句に独立した核を持てるため、1 核モデルの制約を避けられる。
    /// </summary>
    public void SplitWordAtAccent(Voiceroid2WordViewModel wordVm)
    {
        if (wordVm is null) return;

        int k = wordVm.EffectiveAccentPosition;
        if (k <= 0 || k >= wordVm.Moras.Count) return;

        string? newKana = AccentEditKana.SplitWordAtNucleus(wordVm.EditableReading, k);
        if (string.IsNullOrEmpty(newKana)) return;

        var parsed = AccentEditKana.Parse(newKana);
        if (parsed.Count == 0) return;

        int index = Words.IndexOf(wordVm);
        if (index < 0) return;

        var newVms = parsed.Select(w => new Voiceroid2WordViewModel(w)).ToList();
        Words.RemoveAt(index);
        for (int i = 0; i < newVms.Count; i++)
            Words.Insert(index + i, newVms[i]);
    }

    async Task ApplyWordReadingCoreAsync(Voiceroid2WordViewModel wordVm)
    {
        string reading = wordVm.ReadingText?.Trim() ?? string.Empty;
        if (reading.Length == 0) return;
        if (reading == wordVm.EditableReading) return;

        // YMM4 の読み欄 (AquesTalk 記法) と同じ記号が入力され得るため正規化する
        reading = AquesTalkKana.NormalizeYukkuriKana(reading);

        string voiceName = parameter?.VoiceName ?? pronounce.NarratorName;
        if (string.IsNullOrWhiteSpace(voiceName)) return;

        string editable;
        await Voiceroid2EngineGate.Semaphore.WaitAsync();
        try
        {
            editable = await Task.Run(() =>
            {
                AITalkEngine.EnsureOpened(
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvInstallDir),
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvUserDir),
                    voiceName);

                // 合成時と同じパイプライン: かな/漢字 → AI-Kana → 編集用かな
                // (読み欄に入力された「'」は ApplyAccentMarks が AI-Kana へ反映する)
                string plain = AquesTalkKana.StripAccentMarks(reading);
                string aiKana = AITalkEngine.DecodeAnsiText(AITalkEngine.TextToKana(plain));
                aiKana = AquesTalkKana.ApplyAccentMarks(reading, aiKana);
                return AquesTalkKana.ToEditableKana(aiKana);
            });
        }
        finally
        {
            Voiceroid2EngineGate.Semaphore.Release();
        }

        var parsed = AccentEditKana.Parse(editable);
        if (parsed.Count == 0) return;

        // 編集中の単語を再取得した単語 (通常 1 語、句読点を含む場合は複数) へ置き換える。
        // アクセント位置は TextToKana が返すエンジン既定になる (読みが変わったため
        // 以前の核位置は保持しない。VOICEPEAK の再取得と同じ挙動)。
        int index = Words.IndexOf(wordVm);
        if (index < 0) return;

        var newVms = parsed.Select(w => new Voiceroid2WordViewModel(w)).ToList();
        Words.RemoveAt(index);
        for (int i = 0; i < newVms.Count; i++)
            Words.Insert(index + i, newVms[i]);
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
        // 反映待ちの読み編集を先に適用する (入力直後にプレビューしても古い読みで鳴らないように)
        await FlushPendingReadingAppliesAsync();

        // 実際の合成と同じパイプライン: セリフ由来の読みに手動編集を反映する。
        // これによりプレビューと実際の発音が一致する。
        string baseText = pronounce.SourceText;
        if (string.IsNullOrWhiteSpace(baseText))
            baseText = CurrentKana; // 前回レンダーが無い場合は編集用かなをそのまま使う

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

                    string aiKana = AITalkEngine.BuildAiKana(baseText, CurrentKana);
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
