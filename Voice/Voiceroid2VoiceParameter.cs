using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Plugin.Voice;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2 の合成パラメータ。
/// AITalk_TTtsParam.Speaker[0] の volume / speed / pitch / range / pauseSentence に対応する。
/// </summary>
public class Voiceroid2VoiceParameter : VoiceParameterBase
{
    /// <summary>
    /// このパラメータが紐づく声質 (VoiceDB ディレクトリ名)。
    /// <see cref="Voiceroid2VoiceSpeaker.CreateVoiceParameter"/> で speaker 側から設定される。
    /// </summary>
    public string VoiceName { get; set; } = string.Empty;

    double speed = 1.0;

    /// <summary>話速。AITalk 既定は 1.0。</summary>
    [Display(Name = "話速", Description = "テキストの読み上げ速度 (0.5 - 2.0)")]
    [TextBoxSlider("F2", "", 0.5, 2.0, Delay = -1)]
    [Range(0.5, 2.0)]
    [DefaultValue(1.0)]
    public double Speed { get => speed; set => Set(ref speed, value); }

    double pitch;

    /// <summary>ピッチ。AITalk 既定は 0.0。</summary>
    [Display(Name = "ピッチ", Description = "声の高さ (-0.5 - 0.5)")]
    [TextBoxSlider("F2", "", -0.5, 0.5, Delay = -1)]
    [Range(-0.5, 0.5)]
    [DefaultValue(0.0)]
    public double Pitch { get => pitch; set => Set(ref pitch, value); }

    double range = 1.0;

    /// <summary>抑揚。AITalk 既定は 1.0。</summary>
    [Display(Name = "抑揚", Description = "声の抑揚の強さ (0.0 - 2.0)")]
    [TextBoxSlider("F2", "", 0.0, 2.0, Delay = -1)]
    [Range(0.0, 2.0)]
    [DefaultValue(1.0)]
    public double Range { get => range; set => Set(ref range, value); }

    double volume = 1.0;

    /// <summary>音量。AITalk 既定は 1.0。</summary>
    [Display(Name = "音量", Description = "音量 (0.0 - 2.0)")]
    [TextBoxSlider("F2", "", 0.0, 2.0, Delay = -1)]
    [Range(0.0, 2.0)]
    [DefaultValue(1.0)]
    public double Volume { get => volume; set => Set(ref volume, value); }

    double pauseSentence = 300.0;

    /// <summary>文間ポーズ [ms]。AITalk の pauseSentence に対応。</summary>
    [Display(Name = "ポーズ (文間)", Description = "文と文の間のポーズ長 (0 - 2000 ms)")]
    [TextBoxSlider("F0", "ms", 0.0, 2000.0, Delay = -1)]
    [Range(0.0, 2000.0)]
    [DefaultValue(300.0)]
    public double PauseSentence { get => pauseSentence; set => Set(ref pauseSentence, value); }
}
