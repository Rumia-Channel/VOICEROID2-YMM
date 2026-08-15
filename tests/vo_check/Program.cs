// vo_check: AITalk 層 (Voice/AITalk) の検証用コンソール。
// フェイク aitalked.dll (tests/fake_aitalked) に対して P/Invoke 契約・
// コールバック・イベント駆動のジョブ完了・WAV 出力・環境変数駆動の検出を検証する。
// 使い方: 先に tests\fake_aitalked\build.cmd を実行し、次に `dotnet run --project tests/vo_check`。

using System.Globalization;
using Voiceroid2Ymm.Voice.AITalk;

int failures = 0;

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) failures++;
}

static byte[] Concat(params byte[][] arrays)
{
    var result = new byte[arrays.Sum(a => a.Length)];
    int offset = 0;
    foreach (var a in arrays) { a.CopyTo(result, offset); offset += a.Length; }
    return result;
}

static bool ByteEquals(byte[] a, byte[] b)
    => a.Length == b.Length && a.AsSpan().SequenceEqual(b);

static string ShowHex(byte[] bytes)
    => string.Join(" ", bytes.Select(b => b.ToString("X2")));

/// <summary>文字列をシステム ANSI コードページのバイト列へ (CodePages 依存なし)。</summary>
static byte[] AnsiBytes(string s)
{
    IntPtr ptr = Marshal.StringToCoTaskMemAnsi(s);
    try
    {
        var list = new List<byte>();
        for (int i = 0; ; i++)
        {
            byte b = Marshal.ReadByte(ptr, i);
            if (b == 0) break;
            list.Add(b);
        }
        return list.ToArray();
    }
    finally
    {
        Marshal.FreeCoTaskMem(ptr);
    }
}

/// <summary>ログファイル (ANSI) を文字列として読む。</summary>
static string ReadLogAnsi(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
    try
    {
        return Marshal.PtrToStringAnsi(handle.AddrOfPinnedObject(), bytes.Length) ?? string.Empty;
    }
    finally
    {
        handle.Free();
    }
}

static string Ascii(byte[] bytes, int offset, int length)
    => Encoding.ASCII.GetString(bytes, offset, length);

static bool CheckWav(string path, int expectedSamples)
{
    try
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44) return false;
        if (Ascii(bytes, 0, 4) != "RIFF") return false;
        if (Ascii(bytes, 8, 4) != "WAVE") return false;
        if (Ascii(bytes, 12, 4) != "fmt ") return false;
        short format = BitConverter.ToInt16(bytes, 20);
        short channels = BitConverter.ToInt16(bytes, 22);
        int rate = BitConverter.ToInt32(bytes, 24);
        short bits = BitConverter.ToInt16(bytes, 34);
        if (Ascii(bytes, 36, 4) != "data") return false;
        int dataSize = BitConverter.ToInt32(bytes, 40);
        if (format != 1 || channels != 1 || rate != 44100 || bits != 16) return false;
        if (dataSize != expectedSamples * 2) return false;
        if (bytes.Length != 44 + dataSize) return false;
        return true;
    }
    catch
    {
        return false;
    }
}

/// <summary>ANSI バイト列を文字列へ (CodePages 依存なし)。</summary>
static string AnsiToString(byte[] bytes)
{
    if (bytes.Length == 0) return string.Empty;
    var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
    try
    {
        return Marshal.PtrToStringAnsi(handle.AddrOfPinnedObject(), bytes.Length) ?? string.Empty;
    }
    finally
    {
        handle.Free();
    }
}

// ---------------- 実機モード (VOICEROID2 がインストールされた環境での検証) ----------------
// 使い方: vo_check --real <声質名>
// 環境変数 VOICEROID2_AUTH_SEED にシード値を設定して実行する (フェイクは使わない)。
if (args.Length >= 2 && args[0] == "--real")
{
    string realVoice = args[1];
    try
    {
        Console.WriteLine($"install: {AITalkInstallation.DetectInstallDirectory()}");
        Console.WriteLine($"voices: [{string.Join(", ", AITalkInstallation.EnumerateVoiceNames())}]");
        AITalkEngine.EnsureOpened(null, null, realVoice);
        Console.WriteLine("engine opened (real)");

        // ポーズ値 (pauseSentence >= pauseLong が実機 SDK の要件) を設定できるか確認
        AITalkEngine.ApplySpeakerParams(new AITalkSpeakerParams(Volume: 1.0f, Speed: 1.0f, Pitch: 1.0f, Range: 1.0f, PauseSentence: 500));
        Console.WriteLine("speaker params applied (real)");

        string source = "こんにちは、ゆっ'くりしていってね。";
        byte[] kana = AITalkEngine.TextToKana(AquesTalkKana.StripAccentMarks(source));
        string aiKana = AITalkEngine.DecodeAnsiText(kana);
        string edited = AquesTalkKana.ApplyAccentMarks(source, aiKana);
        Console.WriteLine($"kana ({kana.Length} bytes): {aiKana}");
        Console.WriteLine($"edited: {edited}");
        Console.WriteLine($"editable kana: {AquesTalkKana.ToEditableKana(edited)}");
        Check(edited.Contains('^'), "accent mark applied to AI-Kana");

        // ---- 再現調査: 「こんにちは、弦巻マキです」 ----
        string repro = "こんにちは、弦巻マキです";
        byte[] rkana = AITalkEngine.TextToKana(repro);
        string rai = AITalkEngine.DecodeAnsiText(rkana);
        Console.WriteLine($"REPRO texttokana: {rai}");
        Console.WriteLine($"REPRO editable: {AquesTalkKana.ToEditableKana(AquesTalkKana.ApplyAccentMarks(repro, rai))}");

        // ---- 再現調査: YMM4 の読み欄 (AquesTalk 記法) の扱い ----
        // YMM4 は読み欄を text として渡すため、「/」をポーズにしない正規化が必要
        // (正規化なし: ツルマキ$1_1マキデス と余計なポーズが入る)
        string hatsuon = "こんにちわ、つるまき/まきで_ス";
        string fixedKana = AITalkEngine.DecodeAnsiText(
            AITalkEngine.TextToKana(AquesTalkKana.NormalizeYukkuriKana(hatsuon)));
        Console.WriteLine($"HATSUON fixed: {fixedKana}");
        Check(!fixedKana.Contains("$1_1"), "slash (/) no longer becomes a pause");
        Check(fixedKana.Contains("ンニチワ")
            && !fixedKana.Any(c => c is >= '\u3041' and <= '\u3096'), "kana converted to katakana");

        var pcm = AITalkEngine.KanaToSpeech(
            AITalkEngine.EncodeAnsiText(edited),
            new AITalkSpeakerParams(Volume: 1.0f, Speed: 1.0f, Pitch: 1.0f, Range: 1.0f, PauseSentence: 500));
        Console.WriteLine($"pcm samples: {pcm.Length} ({(double)pcm.Length / 44100:F2} s)");
        Check(pcm.Length > 1000, "real synthesis produced audio");

        string wavPath = Path.Combine(Path.GetTempPath(), "vo2_real_out.wav");
        WavFile.WritePcm16Mono(wavPath, pcm);
        Check(CheckWav(wavPath, pcm.Length), "real wav structure");
        Console.WriteLine($"wav: {wavPath}");

        AITalkEngine.Close();
    }
    catch (Exception ex)
    {
        Console.WriteLine("REAL ERROR: " + ex);
        failures++;
    }
    Console.WriteLine(failures == 0 ? "REAL PASS" : "REAL FAIL");
    return failures == 0 ? 0 : 1;
}

Console.WriteLine($"ACP (ANSI code page) = {CultureInfo.CurrentCulture.TextInfo.ANSICodePage}");

// ---------------- フェイク環境の構築 ----------------
string tmp = Path.Combine(Path.GetTempPath(), "vo2_fake_" + Guid.NewGuid().ToString("N"));
string install = Path.Combine(tmp, "install");
string userDir = Path.Combine(tmp, "user");
string outDir = Path.Combine(tmp, "out");
string logPath = Path.Combine(tmp, "fake.log");

Directory.CreateDirectory(Path.Combine(install, "Voice", "akari_44"));
Directory.CreateDirectory(Path.Combine(install, "Lang", "standard"));
Directory.CreateDirectory(Path.Combine(userDir, "単語辞書"));
Directory.CreateDirectory(Path.Combine(userDir, "フレーズ辞書"));
Directory.CreateDirectory(Path.Combine(userDir, "記号ポーズ辞書"));
Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(install, "aitalk.lic"), string.Empty);
File.WriteAllText(Path.Combine(install, "Voice", "akari_44", "akari_44.bin"), string.Empty);
File.WriteAllText(Path.Combine(userDir, "単語辞書", "user.wdic"), string.Empty);
File.WriteAllText(Path.Combine(userDir, "フレーズ辞書", "user.pdic"), string.Empty);
File.WriteAllText(Path.Combine(userDir, "記号ポーズ辞書", "user.sdic"), string.Empty);
string fakeDll = Path.Combine(AppContext.BaseDirectory, "aitalked.dll");
if (!File.Exists(fakeDll))
{
    Console.WriteLine("FAIL fake aitalked.dll not found. Run tests\\fake_aitalked\\build.cmd first.");
    return 1;
}
File.Copy(fakeDll, Path.Combine(install, "aitalked.dll"));

Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, install);
Environment.SetEnvironmentVariable(AITalkInstallation.EnvUserDir, userDir);
Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, "FAKE_SEED_TEST_123");
Environment.SetEnvironmentVariable("AITALK_FAKE_LOG", logPath);

try
{
    // ---------------- 検出 ----------------
    Check(AITalkInstallation.DetectInstallDirectory() == install, "detect install dir via env");
    Check(AITalkInstallation.GetUserDataDirectory() == userDir, "detect user dir via env");
    string[] voices = AITalkInstallation.EnumerateVoiceNames();
    // 実マシンのレジストリ由来のデータ (例: tamiyasu_44) が混ざり得るため包含で判定する
    Check(voices.Contains("akari_44"), $"enumerate voices contains akari_44: [{string.Join(",", voices)}]");

    // ---------------- シード解決 (埋め込みの有無で分岐) ----------------
    // 既定ビルド (埋め込みなし): env 未設定ならエラーになることを確認
    // -p:AuthSeed=... ビルド: env 未設定でも埋め込み値が使われることを確認
    bool threw = false;
    if (string.IsNullOrWhiteSpace(BuildTimeAuthSeed.Value))
    {
        Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, null);
        threw = false;
        try
        {
            AITalkEngine.EnsureOpened(null, null, "akari_44");
        }
        catch (AITalkException ex)
        {
            threw = ex.Message.Contains("VOICEROID2_AUTH_SEED");
        }
        Check(threw, "missing seed throws with env var name");
        Check(!AITalkEngine.IsOpened, "engine stays closed after failed open");
        Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, "FAKE_SEED_TEST_123");
    }
    else
    {
        Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, null);
        Check(AITalkInstallation.GetAuthSeed() == BuildTimeAuthSeed.Value, "embedded seed used when env unset");
        Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, "FAKE_SEED_TEST_123");
    }

    // ---------------- インストール未検出時エラー ----------------
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, Path.Combine(Path.GetTempPath(), "vo2_missing_" + Guid.NewGuid().ToString("N")));
    threw = false;
    try
    {
        AITalkEngine.EnsureOpened(null, null, "akari_44");
    }
    catch (AITalkException)
    {
        threw = true;
    }
    Check(threw, "missing install throws");
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, install);

    // ---------------- 64bit 必須チェック (x86 マーカーの dll を拒否) ----------------
    string install86 = Path.Combine(tmp, "install86");
    Directory.CreateDirectory(Path.Combine(install86, "Voice", "x86voice_44"));
    var x86MarkerDll = new byte[0x100];
    BitConverter.GetBytes(0x80).CopyTo(x86MarkerDll, 0x3C);                    // PE offset
    BitConverter.GetBytes((ushort)0x14C).CopyTo(x86MarkerDll, 0x84);          // IMAGE_FILE_MACHINE_I386 (PE offset 0x80 + 4)
    File.WriteAllBytes(Path.Combine(install86, "aitalked.dll"), x86MarkerDll);
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, install86);
    threw = false;
    try
    {
        AITalkEngine.EnsureOpened(null, null, "x86voice_44");
    }
    catch (AITalkException ex)
    {
        threw = ex.Message.Contains("64bit");
    }
    Check(threw, "x86 aitalked.dll rejected with 64-bit message");
    Check(!AITalkEngine.IsOpened, "engine stays closed after bitness rejection");
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, install);

    // ---------------- 合成フロー ----------------
    AITalkEngine.EnsureOpened(null, null, "akari_44");
    Check(AITalkEngine.IsOpened, "engine opened");

    byte[] kana = AITalkEngine.TextToKana("こんにちは");
    byte[] expectedKana = Concat(AnsiBytes("[H]"), AnsiBytes("こんにちは"), AnsiBytes("[K]"));
    Check(ByteEquals(kana, expectedKana), $"kana round trip [{ShowHex(kana)}]");

    var pcm = AITalkEngine.KanaToSpeech(
        kana,
        new AITalkSpeakerParams(Volume: 1.5f, Speed: 1.2f, Pitch: 1.1f, Range: 1.3f, PauseSentence: 400));
    Check(pcm.Length == 4410, $"pcm sample count ({pcm.Length})");
    bool inRange = pcm.All(s => s >= -10000 && s <= 10000);
    Check(inRange, "pcm samples within ramp range");

    string wavPath = Path.Combine(outDir, "out.wav");
    WavFile.WritePcm16Mono(wavPath, pcm);
    Check(CheckWav(wavPath, 4410), "wav file structure (4410 samples)");

    string emptyWavPath = Path.Combine(outDir, "empty.wav");
    WavFile.WritePcm16Mono(emptyWavPath, ReadOnlySpan<short>.Empty);
    Check(CheckWav(emptyWavPath, 0), "wav file structure (empty)");

    // 声質切替 (同じデータディレクトリ内) と再合成
    AITalkEngine.LoadVoice("akari_44", null);
    var pcm2 = AITalkEngine.KanaToSpeech(kana, new AITalkSpeakerParams(Volume: 1.0f, Speed: 1.0f, Pitch: 1.0f, Range: 1.0f, PauseSentence: 300));
    Check(pcm2.Length == 4410, "second synthesis after voice reload");

    // ---------------- 構造体レイアウト整合 (フェイクのログと比較) ----------------
    int csharpConfigSize = Marshal.SizeOf<AITalk_TConfig>();
    int csharpTtsSize = Marshal.SizeOf<AITalk_TTtsParam>();
    int csharpJobSize = Marshal.SizeOf<AITalk_TJobParam>();

    string log = ReadLogAnsi(logPath);
    Check(log.Contains("sizeofConfig=" + csharpConfigSize), $"struct size AITalk_TConfig ({csharpConfigSize})");
    Check(log.Contains("sizeofTtsParam=" + csharpTtsSize), $"struct size AITalk_TTtsParam ({csharpTtsSize})");
    Check(log.Contains("sizeofJobParam=" + csharpJobSize), $"struct size AITalk_TJobParam ({csharpJobSize})");

    // ---------------- フェイクのログ内容検証 ----------------
    Check(log.Contains("init hz=44100"), "log: init hz");
    Check(log.Contains("seed=FAKE_SEED_TEST_123"), "log: auth seed passed to Init");
    Check(log.Contains("license=" + Path.Combine(install, "aitalk.lic")), "log: license path");
    Check(log.Contains("voiceDbs=" + Path.Combine(install, "Voice")), "log: voice db dir");
    Check(log.Contains("langload " + Path.Combine(install, "Lang", "standard")), "log: langload standard");
    Check(log.Contains("voiceload akari_44"), "log: voiceload");
    Check(log.Contains("reloadword " + Path.Combine(userDir, "単語辞書", "user.wdic")), "log: reloadword");
    Check(log.Contains("reloadphrase " + Path.Combine(userDir, "フレーズ辞書", "user.pdic")), "log: reloadphrase");
    Check(log.Contains("reloadsymbol " + Path.Combine(userDir, "記号ポーズ辞書", "user.sdic")), "log: reloadsymbol");
    Check(log.Contains("texttokana mode=21 text=こんにちは"), "log: texttokana (mode PLAIN_TO_AIKANA)");
    Check(log.Contains("texttospeech mode=12"), "log: texttospeech (mode AIKANA_TO_WAVE)");
    Check(log.Contains("closekana"), "log: closekana");
    Check(log.Contains("closespeech"), "log: closespeech");
    Check(log.Contains("speed=1.2"), "log: speaker speed applied");
    Check(log.Contains("volume=1.5"), "log: speaker volume applied");
    Check(log.Contains("pitch=1.1"), "log: speaker pitch applied");
    Check(log.Contains("range=1.3"), "log: speaker range applied");
    Check(log.Contains("pauseSentence=400"), "log: speaker pauseSentence applied");
    Check(log.Contains("callbacks=111"), "log: all three callbacks set");
    Check(log.Contains("extendFormat=17"), "log: extendFormat JeitaRuby|AutoBookmark");

    AITalkEngine.Close();
    Check(!AITalkEngine.IsOpened, "engine closed");

    // ---------------- 読み編集 (よみがな) の適用パイプライン ----------------
    // アクセントエディタの読み欄編集と同じ順序を模擬する
    // (StripAccentMarks → TextToKana → ApplyAccentMarks → ToEditableKana → Parse)。
    AITalkEngine.EnsureOpened(null, null, "akari_44");

    static string ApplyReadingPipeline(string reading)
        => AquesTalkKana.ToEditableKana(AquesTalkKana.ApplyAccentMarks(
            reading,
            AITalkEngine.DecodeAnsiText(AITalkEngine.TextToKana(AquesTalkKana.StripAccentMarks(reading)))));

    var readingWords = AccentEditKana.Parse(ApplyReadingPipeline("ネコチャンダ"));
    Check(readingWords.Count >= 1 && readingWords[0].Moras.Count == 5,
        $"reading apply: ネコチャンダ → 5 moras (got {(readingWords.Count >= 1 ? readingWords[0].Moras.Count : 0)})");

    var readingWords2 = AccentEditKana.Parse(ApplyReadingPipeline("ネ'コチャンダ"));
    Check(readingWords2.Count >= 1 && readingWords2[0].AccentPosition == 0,
        $"reading apply: ' 付き読みのアクセント位置 0 (got {(readingWords2.Count >= 1 ? readingWords2[0].AccentPosition : -99)})");

    var readingWords3 = AccentEditKana.Parse(ApplyReadingPipeline("ネコ"));
    Check(readingWords3.Count >= 1 && readingWords3[0].Moras.Count == 2,
        $"reading apply: ネコ → 2 moras (got {(readingWords3.Count >= 1 ? readingWords3[0].Moras.Count : 0)})");

    AITalkEngine.Close();
    Check(!AITalkEngine.IsOpened, "engine closed after reading apply checks");

    // ---------------- 読み仮名辞書 (ReadingApplier) ----------------
    var entries = new List<ReadingEntry>
    {
        new() { Surface = "USB", Reading = "ユーエスビー", Enabled = true },
        new() { Surface = "US", Reading = "ウス", Enabled = true },
        new() { Surface = "無効", Reading = "ムコウ", Enabled = false },
        new() { Surface = " ", Reading = "スペース", Enabled = true },
    };
    Check(ReadingApplier.Apply("USBメモリ", entries) == "ユーエスビーメモリ", "applier: longest match wins");
    Check(ReadingApplier.Apply("US", entries) == "ウス", "applier: shorter entry still applies");
    Check(ReadingApplier.Apply("無効なエントリ", entries) == "無効なエントリ", "applier: disabled entry ignored");
    Check(ReadingApplier.Apply("あいうえお", entries) == "あいうえお", "applier: no match unchanged");
    Check(ReadingApplier.Apply(string.Empty, entries) == string.Empty, "applier: empty text");

    // ---------------- AquesTalk アクセント記法 (AquesTalkKana) ----------------
    Check(AquesTalkKana.SplitMorae("コンニチワ").Count == 5, "mora split: コンニチワ");
    Check(AquesTalkKana.SplitMorae("ユックリ").Count == 4, "mora split: ユックリ (ッ は独立)");
    Check(AquesTalkKana.SplitMorae("キャッ").Count == 2, "mora split: キャッ (拗音+促音)");
    Check(AquesTalkKana.SplitMorae("コーヒー").Count == 2, "mora split: コーヒー (ー は連結)");
    Check(AquesTalkKana.SplitMorae("ゆっくり").Count == 4, "mora split: ひらがな");
    Check(AquesTalkKana.StripAccentMarks("ハ'シ") == "ハシ", "strip accent marks");
    Check(AquesTalkKana.ApplyAccentMarks("ハ'シ", "ハシ") == "ハ^シ", "apply accent: ハ'シ");
    Check(AquesTalkKana.ApplyAccentMarks("ハシ'", "ハシ") == "ハシ^", "apply accent: ハシ'");
    Check(AquesTalkKana.ApplyAccentMarks("ハ'シ", "ハ^シ") == "ハ^シ", "replace default accent in same phrase");
    Check(AquesTalkKana.ApplyAccentMarks("カ'メ", "カメラ") == "カメラ", "mora mismatch falls back unchanged");
    Check(AquesTalkKana.ApplyAccentMarks("コ'ンニチワ、サヨウナラ", "コ^ンニチワ$2_2サ^ヨウナラ") == "コ^ンニチワ$2_2サ^ヨウナラ", "accent applied to marked phrase only");
    Check(AquesTalkKana.ToEditableKana("<S>(Irq MARK=_AI@12)コ^ンニチワ$2_2ユ^ック!リ<H>") == "コ'ンニチワ、ユ'ックリ", "AI-Kana to editable kana");
    Check(AquesTalkKana.ToEditableKana("ハシ") == "ハシ", "editable kana passthrough");

    // ---------------- YMM4 読み欄 (AquesTalk 記法) の正規化 ----------------
    Check(AquesTalkKana.NormalizeYukkuriKana("こんにちわ、つるまき/まきで_ス") == "こんにちわ、つるまきまきで!ス",
        "normalize: / 除去と _ → !");
    Check(AquesTalkKana.NormalizeYukkuriKana("デ_ス") == "デ!ス", "normalize: 無声化");
    Check(AquesTalkKana.NormalizeYukkuriKana("こんにちは") == "こんにちは", "normalize: 記号なしは不変");
    Check(AquesTalkKana.NormalizeYukkuriKana(string.Empty) == string.Empty, "normalize: 空");

    // ---------------- アクセントエディタのデータモデル (AccentEditKana) ----------------
    var accentWords = AccentEditKana.Parse("コ'ンニチワ、ユ'ックリ");
    Check(accentWords.Count == 3, "accent parse: 3 words (2 kana + 1 punctuation)");
    Check(accentWords[0].Text == "コンニチワ" && accentWords[0].AccentPosition == 0, "accent parse: word0 position");
    Check(accentWords[0].Moras.Count == 5, "accent parse: word0 morae");
    Check(accentWords[1].IsPunctuation && accentWords[1].Text == "、", "accent parse: punctuation word");
    Check(accentWords[2].AccentPosition == 0, "accent parse: word2 position (ユ'ックリ)");
    Check(AccentEditKana.Build(accentWords) == "コ'ンニチワ、ユ'ックリ", "accent build round trip");

    var accentWords2 = AccentEditKana.Parse("ハシ'");
    Check(accentWords2.Count == 1 && accentWords2[0].AccentPosition == 1, "accent parse: trailing mark");
    Check(AccentEditKana.Build(accentWords2) == "ハシ'", "accent build: trailing mark");

    var accentWords3 = AccentEditKana.Parse("ハシ。");
    Check(accentWords3.Count == 2 && accentWords3[0].AccentPosition == -1 && accentWords3[1].IsPunctuation, "accent parse: no mark + punctuation");
    Check(AccentEditKana.Build(accentWords3) == "ハシ。", "accent build: no mark");

    var accentWords4 = AccentEditKana.Parse(string.Empty);
    Check(accentWords4.Count == 0 && AccentEditKana.Build(accentWords4) == string.Empty, "accent parse: empty");

    // ---------------- 句分割 (AccentEditKana.SplitWordAtNucleus) ----------------
    Check(AccentEditKana.SplitWordAtNucleus("ハシ", 1) == "ハ'、シ'", "split: ハシ@1 → ハ'、シ'");
    Check(AccentEditKana.SplitWordAtNucleus("コンニチワ", 2) == "コン'、ニ'チワ", "split: コンニチワ@2 → コン'、ニ'チワ");
    Check(AccentEditKana.SplitWordAtNucleus("ハシ", 0) is null, "split: 核が先頭なら不可");
    Check(AccentEditKana.SplitWordAtNucleus("ハシ", 2) is null, "split: 核が末尾なら不可");
    var splitWords = AccentEditKana.Parse("コン'、ニ'チワ");
    Check(splitWords.Count == 3
        && splitWords[0].AccentPosition == 1 && splitWords[0].Moras.Count == 2
        && splitWords[1].IsPunctuation
        && splitWords[2].AccentPosition == 0 && splitWords[2].Moras.Count == 3,
        "split: parse → 前半(核1) + 、 + 後半(核0)");
    Check(AccentEditKana.Build(splitWords) == "コン'、ニ'チワ", "split: build round trip");
}
finally
{
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvInstallDir, null);
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvUserDir, null);
    Environment.SetEnvironmentVariable(AITalkInstallation.EnvAuthSeed, null);
    Environment.SetEnvironmentVariable("AITALK_FAKE_LOG", null);
    try { Directory.Delete(tmp, true); } catch { }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
