using YukkuriMovieMaker.Plugin.Voice;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2-YMM プラグインのエントリポイント。
/// aitalked.dll (AITalk SDK) 経由で VOICEROID2 の音声合成を提供する。
/// </summary>
public class Voiceroid2VoicePlugin : IVoicePlugin
{
    /// <summary>YMM4 上に表示されるプラグイン名。</summary>
    public string Name => "VOICEROID2";

    /// <summary>
    /// 声質 (話者) 一覧。Voiceroid2VoiceSettings のキャッシュから復元する。
    /// IVoiceSpeaker は毎回作成して良い。
    /// </summary>
    public IEnumerable<IVoiceSpeaker> Voices
        => Voiceroid2VoiceSettings.Default.VoiceNames
            .Select(n => new Voiceroid2VoiceSpeaker(n));

    /// <summary>Voices を <see cref="UpdateVoicesAsync"/> で更新可能。</summary>
    public bool CanUpdateVoices => true;

    /// <summary>Voices のキャッシュが作成済みなら true。</summary>
    public bool IsVoicesCached => Voiceroid2VoiceSettings.Default.IsVoicesCached;

    /// <summary>声質一覧を再スキャンし、設定ファイルへ保存する。</summary>
    public Task UpdateVoicesAsync() => Voiceroid2VoiceSettings.Default.UpdateVoicesAsync();
}
