using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Commons;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// <see cref="Voiceroid2VoicePronounce.EditKana"/> に付けるアクセント編集 UI。
/// <see cref="Voiceroid2PronounceEditor"/> 内に「VOICEROID2 発音編集...」ボタンを表示し、
/// 押下で <see cref="Voiceroid2AccentEditorWindow"/> を起動する。
/// </summary>
public sealed class Voiceroid2AccentEditorAttribute : PropertyEditorAttribute
{
    public override FrameworkElement Create() => new Voiceroid2PronounceEditor();

    public override void SetBindings(
        FrameworkElement control,
        object item,
        object propertyOwner,
        PropertyInfo propertyInfo)
    {
        if (control is not Voiceroid2PronounceEditor editor) return;

        editor.Parameter = FindParameter(item) ?? FindParameter(propertyOwner);
        editor.Pronounce = propertyOwner as Voiceroid2VoicePronounce;
    }

    public override void ClearBindings(FrameworkElement control)
    {
        if (control is not Voiceroid2PronounceEditor editor) return;

        editor.Pronounce = null;
        editor.Parameter = null;
    }

    /// <summary>
    /// <paramref name="source"/> 自身、またはその public / non-public なプロパティ・フィールド
    /// の中から <see cref="Voiceroid2VoiceParameter"/> 型の値を 1 つ探し返す。
    /// インデックスは走査対象外。再帰はしない。
    /// </summary>
    static Voiceroid2VoiceParameter? FindParameter(object? source)
    {
        if (source is null) return null;

        if (source is Voiceroid2VoiceParameter self)
            return self;

        var type = source.GetType();

        const BindingFlags flags = BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (!prop.CanRead) continue;

            object? value;
            try { value = prop.GetValue(source); }
            catch { continue; }

            if (value is Voiceroid2VoiceParameter vp) return vp;
        }

        foreach (var field in type.GetFields(flags))
        {
            object? value;
            try { value = field.GetValue(source); }
            catch { continue; }

            if (value is Voiceroid2VoiceParameter vp) return vp;
        }

        return null;
    }
}
