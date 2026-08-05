namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// 合成時に aitalked.dll へ渡すスピーカー (話者) パラメータ。
/// AITalk_TTtsParam.Speaker[0] の volume / speed / pitch / range / pauseSentence に対応する。
/// </summary>
internal readonly record struct AITalkSpeakerParams(
    float Volume,
    float Speed,
    float Pitch,
    float Range,
    int PauseSentence);

/// <summary>
/// VOICEROID2 の音声合成ライブラリ (aitalked.dll, AITalk SDK) を操作するエンジン。
/// aitalk_wrapper (C++) の C# 移植。プロセス内で 1 インスタンス (静的状態) とし、
/// 呼び出しは <see cref="Voiceroid2Ymm.Voice.Voiceroid2EngineGate"/> で直列化する前提。
/// </summary>
internal static class AITalkEngine
{
    /// <summary>VoiceDB のサンプリングレート [Hz]。</summary>
    public const int VoiceSampleRate = 44100;

    /// <summary>ライブラリ初期化のタイムアウト [ms] (aitalk_wrapper の TIMEOUT と同じ)。</summary>
    const int InitTimeoutMs = 10000;

    /// <summary>読み変換バッファサイズ [byte] (x64: 64 KiB / x86: 4 KiB)。</summary>
    const uint KanaBufferBytesX64 = 0x10000;
    const uint KanaBufferBytesX86 = 0x1000;

    /// <summary>音声出力バッファサイズ [サンプル]。</summary>
    const uint SpeechBufferSamples = 0x10000;

    /// <summary>1 ジョブの既定タイムアウト [ms]。</summary>
    public const int DefaultJobTimeoutMs = 60000;

    static IntPtr moduleHandle;
    static bool isInitialized;
    static string installDirectory = string.Empty;
    static readonly List<string> dataDirectories = new();
    static string dataDirectory = string.Empty;
    static string languagePath = string.Empty;
    static string phraseDictionaryPath = string.Empty;
    static string wordDictionaryPath = string.Empty;
    static string symbolDictionaryPath = string.Empty;
    static string currentVoiceName = string.Empty;

    static byte[] kanaBuffer = new byte[KanaBufferBytesX64];
    static short[] speechBuffer = new short[SpeechBufferSamples];
    static readonly MemoryStream kanaOutput = new();
    static readonly List<short> speechOutput = new();
    static readonly ManualResetEventSlim closeEvent = new(false);

    // コールバックデリゲートは GC から保護するため静的フィールドに保持する
    static readonly AITalkProcTextBuf procTextBufDelegate = CallbackTextBuf;
    static readonly AITalkProcRawBuf procRawBufDelegate = CallbackRawBuf;
    static readonly AITalkProcEventTts procEventTtsDelegate = CallbackEventTts;

    // 関数ポインタ (解決後は null にならない)
    static Fn_AITalkAPI_CloseKana? fnCloseKana;
    static Fn_AITalkAPI_CloseSpeech? fnCloseSpeech;
    static Fn_AITalkAPI_End? fnEnd;
    static Fn_AITalkAPI_GetData? fnGetData;
    static Fn_AITalkAPI_GetJeitaControl? fnGetJeitaControl;
    static Fn_AITalkAPI_GetKana? fnGetKana;
    static Fn_AITalkAPI_GetParam? fnGetParam;
    static Fn_AITalkAPI_GetStatus? fnGetStatus;
    static Fn_AITalkAPI_Init? fnInit;
    static Fn_AITalkAPI_LangClear? fnLangClear;
    static Fn_AITalkAPI_LangLoad? fnLangLoad;
    static Fn_AITalkAPI_LicenseDate? fnLicenseDate;
    static Fn_AITalkAPI_LicenseInfo? fnLicenseInfo;
    static Fn_AITalkAPI_ModuleFlag? fnModuleFlag;
    static Fn_AITalkAPI_ReloadPhraseDic? fnReloadPhraseDic;
    static Fn_AITalkAPI_ReloadSymbolDic? fnReloadSymbolDic;
    static Fn_AITalkAPI_ReloadWordDic? fnReloadWordDic;
    static Fn_AITalkAPI_SetParam? fnSetParam;
    static Fn_AITalkAPI_TextToKana? fnTextToKana;
    static Fn_AITalkAPI_TextToSpeech? fnTextToSpeech;
    static Fn_AITalkAPI_VersionInfo? fnVersionInfo;
    static Fn_AITalkAPI_VoiceClear? fnVoiceClear;
    static Fn_AITalkAPI_VoiceLoad? fnVoiceLoad;

    /// <summary>ライブラリがロード済みか。</summary>
    public static bool IsOpened => moduleHandle != IntPtr.Zero;

    /// <summary>
    /// ライブラリを開き、指定した声質をロードする。
    /// インストール先・シード値・ユーザーデータは環境変数または自動検出から取得する。
    /// </summary>
    public static void EnsureOpened(string? installDirOverride, string? userDirOverride, string voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
            throw new AITalkException("声質名が指定されていません。");

        string install = AITalkInstallation.DetectInstallDirectory(installDirOverride);
        if (moduleHandle == IntPtr.Zero
            || !string.Equals(install, installDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Open(install, userDirOverride);
        }

        LoadVoice(voiceName, userDirOverride);
    }

    /// <summary>ライブラリを閉じる (テスト用)。</summary>
    public static void Close()
    {
        if (moduleHandle == IntPtr.Zero) return;
        AITalkResultCode result = AITalkResultCode.Success;
        if (isInitialized) result = fnEnd!();
        ReleaseModule();
        if (result != AITalkResultCode.Success)
            throw new AITalkException("AITalk ライブラリの終了に失敗しました。", result);
    }

    /// <summary>
    /// テキストを読み記号 (AI Kana) に変換する。
    /// 戻り値は aitalked.dll が出力したバイト列 (システム ANSI コードページ)。
    /// </summary>
    public static byte[] TextToKana(string text, int timeoutMs = DefaultJobTimeoutMs)
    {
        EnsureModuleLoaded();

        kanaOutput.SetLength(0);
        closeEvent.Reset();

        var jobParam = new AITalk_TJobParam
        {
            modeInOut = AITalkJobInOut.PlainToAiKana,
            userData = IntPtr.Zero,
        };
        int jobId = 0;
        ThrowIfFailed(fnTextToKana!(ref jobId, ref jobParam, text ?? string.Empty), "読み変換の開始に失敗しました");

        bool signaled = WaitForJob(timeoutMs);
        ThrowIfFailed(fnCloseKana!(jobId, 0), "読み変換の終了に失敗しました");

        if (!signaled)
        {
            kanaOutput.SetLength(0);
            throw new AITalkException("読み変換がタイムアウトしました。");
        }
        return kanaOutput.ToArray();
    }

    /// <summary>
    /// 読み記号 (AI Kana) を音声 (16 bit PCM, 44100 Hz, モノラル) に変換する。
    /// </summary>
    public static short[] KanaToSpeech(byte[] kana, in AITalkSpeakerParams speakerParams, int timeoutMs = DefaultJobTimeoutMs)
    {
        EnsureModuleLoaded();

        ApplySpeakerParamsCore(in speakerParams);

        speechOutput.Clear();
        closeEvent.Reset();

        var jobParam = new AITalk_TJobParam
        {
            modeInOut = AITalkJobInOut.AiKanaToWave,
            userData = IntPtr.Zero,
        };
        string kanaText = DecodeAnsi(kana ?? Array.Empty<byte>());
        int jobId = 0;
        ThrowIfFailed(fnTextToSpeech!(ref jobId, ref jobParam, kanaText), "音声合成の開始に失敗しました");

        bool signaled = WaitForJob(timeoutMs);
        ThrowIfFailed(fnCloseSpeech!(jobId, 0), "音声合成の終了に失敗しました");

        if (!signaled)
        {
            speechOutput.Clear();
            throw new AITalkException("音声合成がタイムアウトしました。");
        }
        return speechOutput.ToArray();
    }

    /// <summary>話者パラメータを次回の合成に反映する。</summary>
    public static void ApplySpeakerParams(in AITalkSpeakerParams speakerParams)
    {
        EnsureModuleLoaded();
        ApplySpeakerParamsCore(in speakerParams);
    }

    // ---------------- 内部実装 ----------------

    static void ApplySpeakerParamsCore(in AITalkSpeakerParams p)
    {
        byte[] bytes = GetParamBuffer();
        var param = DeserializeParam(bytes);
        param.procTextBuf = Marshal.GetFunctionPointerForDelegate(procTextBufDelegate);
        param.procRawBuf = Marshal.GetFunctionPointerForDelegate(procRawBufDelegate);
        param.procEventTts = Marshal.GetFunctionPointerForDelegate(procEventTtsDelegate);
        param.extendFormat = ExtendFormat.JeitaRuby | ExtendFormat.AutoBookmark;

        // どのフィールドで拒否されるかを特定できるよう、フィールドごとに設定する
        // (実機 SDK は話者パラメータの範囲検証を行い、不正値で InvalidArgument を返す)
        AITalkResultCode result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException("音声パラメータの設定に失敗しました (コールバックのみ)", result);

        param.Speaker.volume = p.Volume;
        result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"音声パラメータの設定に失敗しました (volume={p.Volume})", result);

        param.Speaker.speed = p.Speed;
        result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"音声パラメータの設定に失敗しました (speed={p.Speed})", result);

        param.Speaker.pitch = p.Pitch;
        result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"音声パラメータの設定に失敗しました (pitch={p.Pitch})", result);

        param.Speaker.range = p.Range;
        result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"音声パラメータの設定に失敗しました (range={p.Range})", result);

        // 実機 SDK は pauseSentence >= pauseLong を要求する (未満は InvalidArgument)。
        // エンジン既定の pauseLong 未満が指定された場合は pauseLong に切り上げる。
        param.Speaker.pauseSentence = Math.Max(p.PauseSentence, param.Speaker.pauseLong);
        result = fnSetParam!(ref param);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"音声パラメータの設定に失敗しました (pauseSentence={p.PauseSentence})", result);
    }

    static bool WaitForJob(int timeoutMs)
        => timeoutMs > 0 ? closeEvent.Wait(timeoutMs) : closeEvent.Wait(Timeout.Infinite);

    static void EnsureModuleLoaded()
    {
        if (moduleHandle == IntPtr.Zero || !isInitialized)
            throw new AITalkException("AITalk ライブラリが初期化されていません。EnsureOpened() を先に呼んでください。");
    }

    static void Open(string install, string? userDirOverride)
    {
        ReleaseModule();

        // 認証シードは DLL ロード前に環境変数から取得する
        // (未設定ならモジュールをロードせずに失敗させる)
        string authSeed = AITalkInstallation.GetAuthSeed();

        installDirectory = install;
        dataDirectories.Clear();
        dataDirectories.AddRange(AITalkInstallation.GetDataDirectories(install));
        dataDirectory = install;

        string modulePath = Path.Combine(install, "aitalked.dll");
        if (!Is64BitDll(modulePath))
        {
            throw new AITalkException(
                $"aitalked.dll が 64bit 版ではありません: {modulePath}\n" +
                "YMM4 は 64bit のため、64bit 版の VOICEROID2 が必要です。");
        }
        moduleHandle = NativeMethods.LoadLibrary(modulePath);
        if (moduleHandle == IntPtr.Zero)
        {
            throw new AITalkException(
                $"aitalked.dll をロードできませんでした: {modulePath}\n" +
                "YMM4 と VOICEROID2 のビット数 (32/64 bit) が一致しているか確認してください。");
        }

        try
        {
            ResolveFunctions();
        }
        catch
        {
            ReleaseModule();
            throw;
        }

        string voiceDbDir = Path.Combine(dataDirectory, "Voice");
        string licensePath = Path.Combine(install, "aitalk.lic");
        var config = new AITalk_TConfig
        {
            hzVoiceDB = VoiceSampleRate,
            dirVoiceDBS = Marshal.StringToCoTaskMemAnsi(voiceDbDir),
            msecTimeout = InitTimeoutMs,
            pathLicense = Marshal.StringToCoTaskMemAnsi(licensePath),
            codeAuthSeed = Marshal.StringToCoTaskMemAnsi(authSeed),
            __reserved__ = 0,
        };
        try
        {
            AITalkResultCode result = fnInit!(ref config);
            if (result != AITalkResultCode.Success)
            {
                ReleaseModule();
                throw new AITalkException("AITalk ライブラリの初期化に失敗しました", result);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(config.dirVoiceDBS);
            Marshal.FreeCoTaskMem(config.pathLicense);
            Marshal.FreeCoTaskMem(config.codeAuthSeed);
        }
        isInitialized = true;

        LoadLanguage("standard");
        LoadUserDictionaries(userDirOverride);
    }

    static void ResolveFunctions()
    {
        fnCloseKana = GetFunction<Fn_AITalkAPI_CloseKana>("AITalkAPI_CloseKana");
        fnCloseSpeech = GetFunction<Fn_AITalkAPI_CloseSpeech>("AITalkAPI_CloseSpeech");
        fnEnd = GetFunction<Fn_AITalkAPI_End>("AITalkAPI_End");
        fnGetData = GetFunction<Fn_AITalkAPI_GetData>("AITalkAPI_GetData");
        fnGetJeitaControl = GetFunction<Fn_AITalkAPI_GetJeitaControl>("AITalkAPI_GetJeitaControl");
        fnGetKana = GetFunction<Fn_AITalkAPI_GetKana>("AITalkAPI_GetKana");
        fnGetParam = GetFunction<Fn_AITalkAPI_GetParam>("AITalkAPI_GetParam");
        fnGetStatus = GetFunction<Fn_AITalkAPI_GetStatus>("AITalkAPI_GetStatus");
        fnInit = GetFunction<Fn_AITalkAPI_Init>("AITalkAPI_Init");
        fnLangClear = GetFunction<Fn_AITalkAPI_LangClear>("AITalkAPI_LangClear");
        fnLangLoad = GetFunction<Fn_AITalkAPI_LangLoad>("AITalkAPI_LangLoad");
        fnLicenseDate = GetFunction<Fn_AITalkAPI_LicenseDate>("AITalkAPI_LicenseDate");
        fnLicenseInfo = GetFunction<Fn_AITalkAPI_LicenseInfo>("AITalkAPI_LicenseInfo");
        fnModuleFlag = GetFunction<Fn_AITalkAPI_ModuleFlag>("AITalkAPI_ModuleFlag");
        fnReloadPhraseDic = GetFunction<Fn_AITalkAPI_ReloadPhraseDic>("AITalkAPI_ReloadPhraseDic");
        fnReloadSymbolDic = GetFunction<Fn_AITalkAPI_ReloadSymbolDic>("AITalkAPI_ReloadSymbolDic");
        fnReloadWordDic = GetFunction<Fn_AITalkAPI_ReloadWordDic>("AITalkAPI_ReloadWordDic");
        fnSetParam = GetFunction<Fn_AITalkAPI_SetParam>("AITalkAPI_SetParam");
        fnTextToKana = GetFunction<Fn_AITalkAPI_TextToKana>("AITalkAPI_TextToKana");
        fnTextToSpeech = GetFunction<Fn_AITalkAPI_TextToSpeech>("AITalkAPI_TextToSpeech");
        fnVersionInfo = GetFunction<Fn_AITalkAPI_VersionInfo>("AITalkAPI_VersionInfo");
        fnVoiceClear = GetFunction<Fn_AITalkAPI_VoiceClear>("AITalkAPI_VoiceClear");
        fnVoiceLoad = GetFunction<Fn_AITalkAPI_VoiceLoad>("AITalkAPI_VoiceLoad");
    }

    /// <summary>
    /// 関数ポインタを解決する。x86 の stdcall 装飾名 (_Name@N) と x64 の無装飾名の両方に対応
    /// (aitalk_wrapper の getFunction() と同じ戦略)。
    /// </summary>
    static T GetFunction<T>(string name) where T : Delegate
    {
        IntPtr address = NativeMethods.GetProcAddress(moduleHandle, name);
        if (address == IntPtr.Zero && name.StartsWith('_'))
        {
            int separator = name.IndexOf('@');
            if (separator > 0)
                address = NativeMethods.GetProcAddress(moduleHandle, name.Substring(1, separator - 1));
        }
        if (address == IntPtr.Zero)
            throw new AITalkException($"aitalked.dll から関数 {name} を解決できませんでした (バージョン非対応の可能性)。");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    /// <summary>
    /// DLL の PE ヘッダから 64bit かどうかを判定する (読み込み前にビット数不一致を検出するため)。
    /// </summary>
    static bool Is64BitDll(string dllPath)
    {
        try
        {
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            stream.Position = 0x3C;
            int peOffset = reader.ReadInt32();
            stream.Position = peOffset + 4;
            ushort machine = reader.ReadUInt16();
            return machine == 0x8664; // IMAGE_FILE_MACHINE_AMD64
        }
        catch (Exception)
        {
            // 読めない場合はロード側のエラーに任せる
            return true;
        }
    }

    static void LoadLanguage(string languageName)
    {
        string? languageDir = null;
        foreach (string dir in dataDirectories)
        {
            string candidate = Path.Combine(dir, "Lang", languageName);
            if (Directory.Exists(candidate)) { languageDir = candidate; break; }
        }
        if (languageDir is null)
            throw new AITalkException($"言語データ {languageName} が見つかりません。");

        // aitalk_wrapper の CdChanger 相当: LangLoad はインストールディレクトリを基準にリソースを探す
        AITalkResultCode result = WithWorkingDirectory(installDirectory, () => fnLangLoad!(languageDir));
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"言語データの読み込みに失敗しました: {languageName}", result);
        languagePath = languageDir;
    }

    static void LoadUserDictionaries(string? userDirOverride)
    {
        string userDir = AITalkInstallation.GetUserDataDirectory(userDirOverride);
        TryLoadDictionary(ref phraseDictionaryPath, userDir, "フレーズ辞書", "user.pdic", p => fnReloadPhraseDic!(p));
        TryLoadDictionary(ref wordDictionaryPath, userDir, "単語辞書", "user.wdic", p => fnReloadWordDic!(p));
        TryLoadDictionary(ref symbolDictionaryPath, userDir, "記号ポーズ辞書", "user.sdic", p => fnReloadSymbolDic!(p));
    }

    /// <summary>
    /// ユーザー辞書を読み込む。ファイルが存在しない場合は静かにスキップする
    /// (aitalk_wrapper の loadPhraseDictionary 等と同じ扱い)。
    /// </summary>
    static void TryLoadDictionary(ref string storedPath, string userDir, string subDir, string fileName, Func<string?, AITalkResultCode> reload)
    {
        string path = Path.Combine(userDir, subDir, fileName);
        if (!File.Exists(path)) { storedPath = string.Empty; return; }

        reload(null);
        AITalkResultCode result = reload(path);
        switch (result)
        {
            case AITalkResultCode.Success:
            case AITalkResultCode.UserDicNoEntry:
                storedPath = path;
                break;
            case AITalkResultCode.FileNotFound:
            case AITalkResultCode.UserDicLocked:
                storedPath = string.Empty;
                break;
            default:
                throw new AITalkException($"辞書の読み込みに失敗しました: {path}", result);
        }
    }

    /// <summary>
    /// 声質をロードする。音声データが別のデータディレクトリにある場合は
    /// 再初期化して切り替える (aitalk_wrapper の loadVoice() と同じ)。
    /// </summary>
    internal static void LoadVoice(string voiceName, string? userDirOverride)
    {
        EnsureModuleLoaded();

        string? voiceDataDirectory = null;
        foreach (string dir in dataDirectories)
        {
            if (Directory.Exists(Path.Combine(dir, "Voice", voiceName)))
            {
                voiceDataDirectory = dir;
                break;
            }
        }
        if (voiceDataDirectory is null)
            throw new AITalkException($"声質 {voiceName} の音声データが見つかりません。");

        AITalkResultCode result;
        if (!string.Equals(voiceDataDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // 別の VoiceDB ディレクトリへ切り替える
            string previous = dataDirectory;

            result = fnEnd!();
            isInitialized = false;
            currentVoiceName = string.Empty;
            if (result != AITalkResultCode.Success)
                throw new AITalkException("AITalk ライブラリの終了に失敗しました", result);

            result = InitializeWithDataDirectory(voiceDataDirectory);
            if (result == AITalkResultCode.Success) result = ReloadResources();
            if (result != AITalkResultCode.Success)
            {
                // 切替失敗時は元のディレクトリへ復元する
                if (isInitialized) { fnEnd!(); isInitialized = false; }
                AITalkResultCode restore = InitializeWithDataDirectory(previous);
                if (restore == AITalkResultCode.Success) restore = ReloadResources();
                if (restore != AITalkResultCode.Success)
                {
                    if (isInitialized) fnEnd!();
                    ReleaseModule();
                }
                throw new AITalkException($"声質 {voiceName} へのデータ切替に失敗しました", result);
            }
        }
        else if (!string.IsNullOrEmpty(currentVoiceName))
        {
            result = fnVoiceClear!();
            currentVoiceName = string.Empty;
            if (result != AITalkResultCode.Success)
                throw new AITalkException("声質データの解放に失敗しました", result);
        }

        result = fnVoiceLoad!(voiceName);
        if (result != AITalkResultCode.Success)
            throw new AITalkException($"声質 {voiceName} の読み込みに失敗しました", result);
        currentVoiceName = voiceName;

        ApplyDefaultParam();
    }

    static AITalkResultCode InitializeWithDataDirectory(string directory)
    {
        string authSeed = AITalkInstallation.GetAuthSeed();
        string voiceDbDir = Path.Combine(directory, "Voice");
        string licensePath = Path.Combine(installDirectory, "aitalk.lic");
        var config = new AITalk_TConfig
        {
            hzVoiceDB = VoiceSampleRate,
            dirVoiceDBS = Marshal.StringToCoTaskMemAnsi(voiceDbDir),
            msecTimeout = InitTimeoutMs,
            pathLicense = Marshal.StringToCoTaskMemAnsi(licensePath),
            codeAuthSeed = Marshal.StringToCoTaskMemAnsi(authSeed),
            __reserved__ = 0,
        };
        try
        {
            AITalkResultCode result = fnInit!(ref config);
            isInitialized = result == AITalkResultCode.Success;
            if (isInitialized) dataDirectory = directory;
            return result;
        }
        finally
        {
            Marshal.FreeCoTaskMem(config.dirVoiceDBS);
            Marshal.FreeCoTaskMem(config.pathLicense);
            Marshal.FreeCoTaskMem(config.codeAuthSeed);
        }
    }

    static AITalkResultCode ReloadResources()
    {
        if (!string.IsNullOrEmpty(languagePath))
        {
            AITalkResultCode result = WithWorkingDirectory(installDirectory, () => fnLangLoad!(languagePath));
            if (result != AITalkResultCode.Success) return result;
        }
        if (!string.IsNullOrEmpty(phraseDictionaryPath))
        {
            AITalkResultCode result = fnReloadPhraseDic!(phraseDictionaryPath);
            if (result is not (AITalkResultCode.Success or AITalkResultCode.UserDicNoEntry)) return result;
        }
        if (!string.IsNullOrEmpty(wordDictionaryPath))
        {
            AITalkResultCode result = fnReloadWordDic!(wordDictionaryPath);
            if (result is not (AITalkResultCode.Success or AITalkResultCode.UserDicNoEntry)) return result;
        }
        if (!string.IsNullOrEmpty(symbolDictionaryPath))
        {
            AITalkResultCode result = fnReloadSymbolDic!(symbolDictionaryPath);
            if (result is not (AITalkResultCode.Success or AITalkResultCode.UserDicNoEntry)) return result;
        }
        return AITalkResultCode.Success;
    }

    /// <summary>
    /// VoiceLoad 後に GetParam で初期値を取得し、コールバックと拡張フォーマットを設定して SetParam する
    /// (aitalk_wrapper の loadVoice() 末尾と同じ)。
    /// </summary>
    static void ApplyDefaultParam()
    {
        byte[] bytes = GetParamBuffer();
        var param = DeserializeParam(bytes);
        param.procTextBuf = Marshal.GetFunctionPointerForDelegate(procTextBufDelegate);
        param.procRawBuf = Marshal.GetFunctionPointerForDelegate(procRawBufDelegate);
        param.procEventTts = Marshal.GetFunctionPointerForDelegate(procEventTtsDelegate);
        if (IntPtr.Size == 8) param.lenTextBufBytes = KanaBufferBytesX64;
        param.extendFormat = ExtendFormat.JeitaRuby | ExtendFormat.AutoBookmark;
        ThrowIfFailed(fnSetParam!(ref param), "音声パラメータの初期化に失敗しました");
    }

    /// <summary>GetParam で現在のパラメータをバッファとして取得する。</summary>
    static byte[] GetParamBuffer()
    {
        uint size = 0;
        AITalkResultCode result = fnGetParam!(IntPtr.Zero, ref size);
        if (result != AITalkResultCode.Insufficient || size < Marshal.SizeOf<AITalk_TTtsParam>())
            throw new AITalkException("音声パラメータのサイズ取得に失敗しました", result);

        var bytes = new byte[size];
        // aitalk_wrapper と同じく、2 回目の GetParam 前に構造体先頭の size フィールド
        // (バッファ容量) を設定する。実機 SDK はこれを参照するため、0 のままだと
        // AITALKERR_INSUFFICIENT が返る。
        BitConverter.GetBytes(size).CopyTo(bytes, 0);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            result = fnGetParam!(handle.AddrOfPinnedObject(), ref size);
        }
        finally
        {
            handle.Free();
        }
        if (result != AITalkResultCode.Success)
            throw new AITalkException("音声パラメータの取得に失敗しました", result);
        return bytes;
    }

    static AITalk_TTtsParam DeserializeParam(byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<AITalk_TTtsParam>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    static void ThrowIfFailed(AITalkResultCode result, string message)
    {
        if (result != AITalkResultCode.Success)
            throw new AITalkException(message, result);
    }

    static void ReleaseModule()
    {
        if (moduleHandle != IntPtr.Zero)
        {
            NativeMethods.FreeLibrary(moduleHandle);
            moduleHandle = IntPtr.Zero;
        }
        isInitialized = false;
        installDirectory = string.Empty;
        dataDirectories.Clear();
        dataDirectory = string.Empty;
        languagePath = string.Empty;
        phraseDictionaryPath = string.Empty;
        wordDictionaryPath = string.Empty;
        symbolDictionaryPath = string.Empty;
        currentVoiceName = string.Empty;

        fnCloseKana = null;
        fnCloseSpeech = null;
        fnEnd = null;
        fnGetData = null;
        fnGetJeitaControl = null;
        fnGetKana = null;
        fnGetParam = null;
        fnGetStatus = null;
        fnInit = null;
        fnLangClear = null;
        fnLangLoad = null;
        fnLicenseDate = null;
        fnLicenseInfo = null;
        fnModuleFlag = null;
        fnReloadPhraseDic = null;
        fnReloadSymbolDic = null;
        fnReloadWordDic = null;
        fnSetParam = null;
        fnTextToKana = null;
        fnTextToSpeech = null;
        fnVersionInfo = null;
        fnVoiceClear = null;
        fnVoiceLoad = null;
    }

    /// <summary>一時的にカレントディレクトリを変更して実行する (aitalk_wrapper の CdChanger 相当)。</summary>
    static T WithWorkingDirectory<T>(string path, Func<T> action)
    {
        string previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = path;
        try
        {
            return action();
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    // ---------------- コールバック (aitalk_wrapper の callbackTextBuf / callbackRawBuf / callbackEventTts) ----------------

    static int CallbackTextBuf(AITalkEventReasonCode reasonCode, int jobId, IntPtr userData)
    {
        if (reasonCode is AITalkEventReasonCode.TextBufFull or AITalkEventReasonCode.TextBufFlush or AITalkEventReasonCode.TextBufClose)
        {
            byte[] buffer = kanaBuffer;
            uint bufferSize = (uint)buffer.Length;
            do
            {
                AITalkResultCode result = fnGetKana!(jobId, buffer, bufferSize, out uint readBytes, out _);
                if (result != AITalkResultCode.Success) break;
                kanaOutput.Write(buffer, 0, (int)readBytes);
                if (readBytes < bufferSize - 1) break;
            }
            while (true);
            if (reasonCode != AITalkEventReasonCode.TextBufClose) return 0;
        }
        closeEvent.Set();
        return 0;
    }

    static int CallbackRawBuf(AITalkEventReasonCode reasonCode, int jobId, ulong tick, IntPtr userData)
    {
        if (reasonCode is AITalkEventReasonCode.RawBufFull or AITalkEventReasonCode.RawBufFlush or AITalkEventReasonCode.RawBufClose)
        {
            short[] buffer = speechBuffer;
            uint bufferSize = (uint)buffer.Length;
            do
            {
                AITalkResultCode result = fnGetData!(jobId, buffer, bufferSize, out uint readSamples);
                if (result != AITalkResultCode.Success) break;
                for (int i = 0; i < readSamples; i++) speechOutput.Add(buffer[i]);
                if (readSamples < bufferSize) break;
            }
            while (true);
            if (reasonCode != AITalkEventReasonCode.RawBufClose) return 0;
        }
        closeEvent.Set();
        return 0;
    }

    static int CallbackEventTts(AITalkEventReasonCode reasonCode, int jobId, ulong tick, IntPtr name, IntPtr userData)
    {
        // イベント通知 (発音記号・文節位置) は合成データに影響しないため何もしない
        return 0;
    }

    /// <summary>表示用文字列を読み記号 (AI-Kana) バイト列へ変換する (OS の ANSI コードページ)。</summary>
    public static byte[] EncodeAnsiText(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
        IntPtr ptr = Marshal.StringToCoTaskMemAnsi(text);
        try
        {
            var list = new List<byte>(text.Length * 2);
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

    /// <summary>読み記号 (AI-Kana) バイト列を表示用文字列へ変換する。</summary>
    public static string DecodeAnsiText(byte[] bytes)
        => DecodeAnsi(bytes ?? Array.Empty<byte>());

    /// <summary>
    /// ANSI バイト列を文字列へデコードする。OS の ANSI コードページ (GetACP) を使うため、
    /// System.Text.Encoding.CodePages のような追加依存は不要。
    /// </summary>
    static string DecodeAnsi(byte[] bytes)
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
}
