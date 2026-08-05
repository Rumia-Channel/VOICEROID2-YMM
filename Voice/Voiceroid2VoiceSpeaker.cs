using Voiceroid2Ymm.Voice.AITalk;
using YukkuriMovieMaker.Plugin.Voice;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2 の声質 1 つに対応する <see cref="IVoiceSpeaker"/> 実装。
/// 内部の <see cref="AITalkEngine"/> (aitalked.dll) を介して WAV を生成する。
/// </summary>
public class Voiceroid2VoiceSpeaker : IVoiceSpeaker
{
    /// <summary>
    /// よく知られた声質コード → 表示名。未登録のコードはそのまま表示される。
    /// (例: akari_44 = 紲星あかり)
    /// </summary>
    static readonly IReadOnlyDictionary<string, string> KnownVoiceNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["akari_44"] = "紲星あかり",
        ["tamiyasu_44"] = "民安ともえ",
    };

    /// <summary>音声合成エンジンの表示名。</summary>
    public string EngineName => "VOICEROID2";

    /// <summary>話者の表示名。</summary>
    public string SpeakerName => KnownVoiceNames.TryGetValue(VoiceName, out string? display) ? display : VoiceName;

    /// <summary>音声合成エンジンの識別子 (他プラグインと被らないこと)。</summary>
    public string API => "VOICEROID2-YMM";

    /// <summary>話者の識別子 (= VoiceDB ディレクトリ名)。</summary>
    public string ID => VoiceName;

    /// <summary>商用ソフト (VOICEROID2) で生成するため true。</summary>
    public bool IsVoiceDataCachingRequired => true;

    /// <summary>サポートするテキスト書式。VOICEROID2 は通常の日本語テキストを話す。</summary>
    public SupportedTextFormat Format => SupportedTextFormat.Text;

    /// <summary>音声合成前に利用規約同意が必要ならインスタンスを返す (未使用)。</summary>
    public IVoiceLicense? License => null;

    /// <summary>音声合成前に追加リソースのダウンロードが必要ならインスタンスを返す (未使用)。</summary>
    public IVoiceResource? Resource => null;

    /// <summary>VoiceDB ディレクトリ名 (例: "akari_44")。</summary>
    public string VoiceName { get; }

    public Voiceroid2VoiceSpeaker(string voiceName) => VoiceName = voiceName;

    /// <summary>API / ID がこの話者と一致するか。</summary>
    public bool IsMatch(string api, string id) => api == API && id == ID;

    public IVoiceParameter CreateVoiceParameter()
        => new Voiceroid2VoiceParameter { VoiceName = VoiceName };

    public IVoiceParameter MigrateParameter(IVoiceParameter currentParameter)
        => currentParameter is Voiceroid2VoiceParameter p ? p : CreateVoiceParameter();

    /// <summary>
    /// VOICEROID2 側の読み変換は <see cref="CreateVoiceAsync"/> 内で行うため未使用。
    /// </summary>
    public Task<string> ConvertKanjiToYomiAsync(string text, IVoiceParameter voiceParameter)
        => Task.FromResult(text);

    /// <summary>
    /// aitalked.dll を介してテキストを WAV ファイルへ合成する。
    /// 読み仮名辞書の適用と話者パラメータの反映はここで行う。
    /// </summary>
    public async Task<IVoicePronounce?> CreateVoiceAsync(
        string text, IVoicePronounce? pronounce, IVoiceParameter? parameter, string filePath)
    {
        var param = parameter as Voiceroid2VoiceParameter
            ?? (Voiceroid2VoiceParameter)CreateVoiceParameter();

        // (1) 読み仮名辞書を適用 (テキスト置換)
        var settings = Voiceroid2VoiceSettings.Default;
        string speakText = ReadingApplier.Apply(text, settings.ReadingEntries);

        // (2) 合成 (aitalked.dll へのアクセスは共有ゲートで排他する)
        await Voiceroid2EngineGate.Semaphore.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(speakText))
                {
                    // 空テキストはヘッダのみの WAV を書き出す
                    WavFile.WritePcm16Mono(filePath, ReadOnlySpan<short>.Empty);
                    return new Voiceroid2VoicePronounce
                    {
                        SourceText = speakText,
                        NarratorName = VoiceName,
                    };
                }

                AITalkEngine.EnsureOpened(
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvInstallDir),
                    AITalkInstallation.EnvValue(AITalkInstallation.EnvUserDir),
                    VoiceName);

                var kana = AITalkEngine.TextToKana(speakText);
                var pcm = AITalkEngine.KanaToSpeech(kana, new AITalkSpeakerParams(
                    Volume: (float)param.Volume,
                    Speed: (float)param.Speed,
                    Pitch: (float)param.Pitch,
                    Range: (float)param.Range,
                    PauseSentence: (int)param.PauseSentence));

                WavFile.WritePcm16Mono(filePath, pcm);
                return new Voiceroid2VoicePronounce
                {
                    SourceText = speakText,
                    NarratorName = VoiceName,
                    Kana = kana,
                };
            });
        }
        finally
        {
            Voiceroid2EngineGate.Semaphore.Release();
        }
    }
}
