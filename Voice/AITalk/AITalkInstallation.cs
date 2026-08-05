using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// VOICEROID2 (AITalk SDK) のインストール先・データ配置の検出。
/// ユーザー固有のパスはコードに持たず、環境変数または標準の検出手段のみを使う。
/// </summary>
internal static class AITalkInstallation
{
    /// <summary>認証コードのシード値を指定する環境変数。値はリポジトリの code.jpg に記載。</summary>
    public const string EnvAuthSeed = "VOICEROID2_AUTH_SEED";

    /// <summary>VOICEROID2 インストール先の上書き環境変数 (未設定なら自動検出)。</summary>
    public const string EnvInstallDir = "VOICEROID2_YMM_INSTALL_DIR";

    /// <summary>VOICEROID2 ユーザーデータ (辞書類) の基準ディレクトリの上書き環境変数 (未設定なら %USERPROFILE%\Documents\VOICEROID2)。</summary>
    public const string EnvUserDir = "VOICEROID2_YMM_USER_DIR";

    static readonly Regex VoiceNamePattern = new("^[0-9A-Za-z_]+$", RegexOptions.Compiled);

    /// <summary>環境変数の値を取得する (空 / 空白は null 扱い)。</summary>
    public static string? EnvValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// aitalked.dll のあるインストールディレクトリを検出する。
    /// 優先順: 明示指定 &gt; 環境変数 <see cref="EnvInstallDir"/> &gt; Program Files &gt; レジストリ。
    /// </summary>
    public static string DetectInstallDirectory(string? overridePath = null)
    {
        var candidates = new List<string>();

        string? explicitPath = overridePath ?? EnvValue(EnvInstallDir);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            candidates.Add(explicitPath.Trim().TrimEnd('\\', '/'));

        AddProgramFilesCandidates(candidates);
        AddRegistryCandidates(candidates);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsInstallDirectory(candidate)) return candidate;
        }

        throw new AITalkException(
            $"VOICEROID2 のインストール先が見つかりませんでした。\n" +
            $"インストール済みの場合は環境変数 {EnvInstallDir} で直接指定できます。");
    }

    /// <summary>
    /// VOICEROID2 のユーザーデータディレクトリ (フレーズ辞書・単語辞書・記号ポーズ辞書の置き場所)。
    /// 優先順: 明示指定 &gt; 環境変数 <see cref="EnvUserDir"/> &gt; %USERPROFILE%\Documents\VOICEROID2。
    /// </summary>
    public static string GetUserDataDirectory(string? overridePath = null)
    {
        string? explicitPath = overridePath ?? EnvValue(EnvUserDir);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath.Trim().TrimEnd('\\', '/');

        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new AITalkException($"環境変数 USERPROFILE が取得できませんでした。{EnvUserDir} で直接指定してください。");
        return Path.Combine(userProfile, "Documents", "VOICEROID2");
    }

    /// <summary>
    /// 認証シード値を解決する。
    /// 優先順: 実行時環境変数 <see cref="EnvAuthSeed"/> &gt; ビルド時埋め込み
    /// (<see cref="BuildTimeAuthSeed"/>, csproj の AuthSeed プロパティで生成)。
    /// どちらも無い場合は例外 (値はリポジトリの code.jpg に記載)。
    /// </summary>
    public static string GetAuthSeed()
    {
        string? seed = EnvValue(EnvAuthSeed);
        if (string.IsNullOrWhiteSpace(seed))
            seed = BuildTimeAuthSeed.Value;
        if (string.IsNullOrWhiteSpace(seed))
        {
            throw new AITalkException(
                $"認証コードが設定されていません。\n" +
                $"実行時環境変数 {EnvAuthSeed} を設定するか、ビルド時に -p:AuthSeed=... で埋め込んでください。\n" +
                $"シード値はリポジトリの code.jpg に記載されています。");
        }
        return seed;
    }

    /// <summary>設定画面用の認証コード状態 (値そのものは返さない)。</summary>
    public static string GetAuthSeedStatus()
    {
        if (EnvValue(EnvAuthSeed) is not null)
            return $"環境変数 {EnvAuthSeed} に設定済み";
        if (!string.IsNullOrWhiteSpace(BuildTimeAuthSeed.Value))
            return $"ビルド時に埋め込み済み (実行時環境変数は未設定)";
        return $"未設定 (環境変数 {EnvAuthSeed} またはビルド時埋め込みが必要)";
    }

    /// <summary>
    /// インストール済みの声質 (VoiceDB ディレクトリ名) 一覧を返す。
    /// データ置き場はインストール先と異なる場所 (Program Files 両方・レジストリ指定) も対象にする。
    /// </summary>
    public static string[] EnumerateVoiceNames(string? installDirOverride = null)
    {
        string install = DetectInstallDirectory(installDirOverride);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string dataDir in GetDataDirectories(install))
        {
            string voiceDir = Path.Combine(dataDir, "Voice");
            if (!Directory.Exists(voiceDir)) continue;
            foreach (string dir in Directory.EnumerateDirectories(voiceDir))
            {
                string name = Path.GetFileName(dir);
                if (!VoiceNamePattern.IsMatch(name)) continue;
                if (Directory.EnumerateFileSystemEntries(dir).Any()) names.Add(name);
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// 音声データ (Voice / Lang) を探す対象ディレクトリ一覧。
    /// aitalk_wrapper の getDataDirectories() に対応。
    /// </summary>
    public static IReadOnlyList<string> GetDataDirectories(string installDirectory)
    {
        var directories = new List<string>();
        AddDataDirectory(directories, installDirectory);

        string? programFiles64 = Environment.GetEnvironmentVariable("ProgramW6432");
        string? programFiles32 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        foreach (string? pf in new[] { programFiles64, programFiles32, programFiles }.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(pf))
                AddDataDirectory(directories, Path.Combine(pf, "AHS", "VOICEROID2"));
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            AddRegistryDataDirectory(directories, view);

        return directories;
    }

    static bool IsInstallDirectory(string path)
        => Directory.Exists(path) && File.Exists(Path.Combine(path, "aitalked.dll"));

    static void AddProgramFilesCandidates(List<string> candidates)
    {
        // プロセスと同一ビットの Program Files を優先 (aitalked.dll はビット数が一致するものしかロードできない)
        string? programFiles64 = Environment.GetEnvironmentVariable("ProgramW6432");
        string? programFiles32 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        var ordered = IntPtr.Size == 8
            ? new[] { programFiles64, programFiles32, programFiles }
            : new[] { programFiles32, programFiles64, programFiles };
        foreach (string? pf in ordered.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(pf))
                candidates.Add(Path.Combine(pf, "AHS", "VOICEROID2"));
        }
    }

    static void AddRegistryCandidates(List<string> candidates)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            AddRegistryDataDirectory(candidates, view);
    }

    /// <summary>
    /// レジストリ HKLM\SOFTWARE\AHS\VOICEROID\2.0\Lang\InstallDir からデータルートを復元する。
    /// 値が "...\Lang" なら親、"...\Lang\standard" 等の深いパスなら "Voice" を含む祖先まで遡る。
    /// </summary>
    static void AddRegistryDataDirectory(List<string> directories, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\AHS\VOICEROID\2.0\Lang");
            string? installDir = key?.GetValue("InstallDir") as string;
            if (string.IsNullOrWhiteSpace(installDir)) return;
            string normalized = installDir.Trim().TrimEnd('\\', '/');

            string candidate = normalized;
            for (int i = 0; i < 3; i++)
            {
                if (Directory.Exists(Path.Combine(candidate, "Voice"))) break;
                string? parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || parent == candidate) { candidate = normalized; break; }
                candidate = parent;
            }
            directories.Add(candidate);
        }
        catch (Exception)
        {
            // レジストリが読めなくても検出を継続する
        }
    }

    static void AddDataDirectory(List<string> directories, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        if (directories.Any(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase))) return;
        directories.Add(path);
    }
}
