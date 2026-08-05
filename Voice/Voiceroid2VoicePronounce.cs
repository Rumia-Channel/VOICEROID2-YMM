using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Voiceroid2Ymm.Voice.AITalk;
using Voiceroid2Ymm.Voice.PropertyEditor;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.UndoRedo;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2 の合成結果 (編集用かな) を保持する <see cref="IVoicePronounce"/> 実装。
/// アクセントエディタ (VOICEPEAK-plus のアクセント画面の移植) で編集した内容は
/// <see cref="EditKana"/> に保存され、次回合成時に反映される。
/// </summary>
public sealed class Voiceroid2VoicePronounce : UndoRedoable, IVoicePronounce
{
    string sourceText = string.Empty;
    string narratorName = string.Empty;
    string editKana = string.Empty;
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

    /// <summary>
    /// 編集用かな (アクセントマーク「'」付き)。「VOICEROID2 発音編集 (アクセント)」ボタンで
    /// アクセント位置を編集できる。
    /// </summary>
    [Display(Name = " ", Description = "VOICEROID2 の発音・アクセント編集")]
    [Voiceroid2AccentEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
    [DefaultValue("")]
    public string EditKana
    {
        get => editKana;
        set => Set(ref editKana, value ?? string.Empty);
    }

    /// <summary>アクセントエディタでの手動編集が有効か。</summary>
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

    /// <summary>編集内容をまとめて設定する (アクセントエディタの OK / キャンセル用)。</summary>
    public void SetEditKana(string text, string narrator, string kana, bool manual)
    {
        SourceText = text;
        NarratorName = narrator;
        EditKana = kana;
        IsManualEdit = manual;
    }

    public IVoicePronounce Clone()
        => new Voiceroid2VoicePronounce
        {
            SourceText = SourceText,
            NarratorName = NarratorName,
            EditKana = EditKana,
            IsManualEdit = IsManualEdit,
            LipSyncFrames = LipSyncFrames?.ToArray(),
        };

    public void BeginEdit()
    {
    }

    public ValueTask EndEditAsync()
        => ValueTask.CompletedTask;
}
