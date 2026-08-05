using System.Collections.ObjectModel;
using Voiceroid2Ymm.Voice.AITalk;
using Voiceroid2Ymm.Voice.Settings;
using YukkuriMovieMaker.Plugin;

namespace Voiceroid2Ymm.Voice;

/// <summary>
/// VOICEROID2-YMM の設定。
/// 声質一覧とプラグイン共通の読み仮名辞書をキャッシュする。
/// パスやシード値は設定に保存せず、環境変数または自動検出で解決する。
/// </summary>
public class Voiceroid2VoiceSettings : SettingsBase<Voiceroid2VoiceSettings>
{
    /// <summary>設定カテゴリ。ボイス系は <see cref="SettingsCategory.Voice"/>。</summary>
    public override SettingsCategory Category => SettingsCategory.Voice;

    /// <summary>設定名。</summary>
    public override string Name => "VOICEROID2";

    /// <summary>設定画面を表示する。</summary>
    public override bool HasSettingView => true;

    Voiceroid2SettingsView? settingView;
    public override object SettingView => settingView ??= new Voiceroid2SettingsView { DataContext = this };

    // ---------------- Voice cache ----------------

    bool isVoicesCached;
    string[] voiceNames = [];

    /// <summary>
    /// 声質一覧のキャッシュが作成済みか。
    /// false の場合、YMM4 起動時に <see cref="UpdateVoicesAsync"/> が呼ばれる。
    /// </summary>
    public bool IsVoicesCached
    {
        get => isVoicesCached && VoiceNames.Length > 0;
        set => Set(ref isVoicesCached, value);
    }

    /// <summary>声質 (VoiceDB ディレクトリ名) の一覧。</summary>
    public string[] VoiceNames
    {
        get => voiceNames;
        set => Set(ref voiceNames, (value ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToArray());
    }

    // ---------------- Reading dictionary ----------------

    ObservableCollection<ReadingEntry> readingEntries = new();

    /// <summary>
    /// 読み仮名辞書。表記 → 読み (カタカナ) のマップ。
    /// 合成時に <see cref="ReadingApplier"/> でテキスト置換される。
    /// </summary>
    public ObservableCollection<ReadingEntry> ReadingEntries
    {
        get => readingEntries;
        set => Set(ref readingEntries, value);
    }

    public override void Initialize()
    {
    }

    // ---------------- Voice refresh ----------------

    /// <summary>
    /// インストール先の Voice ディレクトリをスキャンして声質一覧を更新し、キャッシュへ保存する。
    /// </summary>
    public Task UpdateVoicesAsync() => Task.Run(() =>
    {
        VoiceNames = AITalkInstallation.EnumerateVoiceNames();
        IsVoicesCached = VoiceNames.Length > 0;
        Save();
    });
}
