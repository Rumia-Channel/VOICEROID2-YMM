using System.ComponentModel;
using Voiceroid2Ymm.Voice.AITalk;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.UndoRedo;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2 の合成結果 (読み記号) を保持する <see cref="IVoicePronounce"/> 実装。
/// v1 では合成の再現用メタデータとして使う (手動編集 UI は未実装)。
/// </summary>
public sealed class Voiceroid2VoicePronounce : UndoRedoable, IVoicePronounce
{
    string sourceText = string.Empty;
    string narratorName = string.Empty;
    byte[] kana = Array.Empty<byte>();
    bool isManualEdit;

    /// <summary>合成対象テキスト (読み仮名辞書適用後)。</summary>
    [Browsable(false)]
    public string SourceText
    {
        get => sourceText;
        set => Set(ref sourceText, value ?? string.Empty);
    }

    /// <summary>声質名 (VoiceDB ディレクトリ名)。</summary>
    [Browsable(false)]
    public string NarratorName
    {
        get => narratorName;
        set => Set(ref narratorName, value ?? string.Empty);
    }

    /// <summary>aitalked.dll が出力した読み記号 (AI Kana, ANSI バイト列)。</summary>
    [Browsable(false)]
    public byte[] Kana
    {
        get => kana;
        set => Set(ref kana, value ?? Array.Empty<byte>());
    }

    /// <summary>v1 では常に false (自動生成のみ)。</summary>
    [Browsable(false)]
    public bool IsManualEdit
    {
        get => isManualEdit;
        set => Set(ref isManualEdit, value);
    }

    /// <summary>リップシンクデータ (未使用)。</summary>
    [Browsable(false)]
    public LipSyncFrame[]? LipSyncFrames { get; set; }

    /// <summary>同じテキスト・声質に対する合成結果か。</summary>
    public bool Matches(string text, string narrator)
        => string.Equals(SourceText, text, StringComparison.Ordinal)
        && string.Equals(NarratorName, narrator, StringComparison.Ordinal);

    public IVoicePronounce Clone()
        => new Voiceroid2VoicePronounce
        {
            SourceText = SourceText,
            NarratorName = NarratorName,
            Kana = (byte[])Kana.Clone(),
            IsManualEdit = IsManualEdit,
            LipSyncFrames = LipSyncFrames?.ToArray(),
        };

    public void BeginEdit()
    {
    }

    public ValueTask EndEditAsync()
        => ValueTask.CompletedTask;
}
