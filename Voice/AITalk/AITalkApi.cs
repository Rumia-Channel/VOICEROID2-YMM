namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// AITalk SDK (aitalked.dll) の結果コード。aitalk_AITalk.h の AITalkResultCode に対応。
/// </summary>
internal enum AITalkResultCode
{
    Success = 0,
    InternalError = -1,
    Unsupported = -2,
    InvalidArgument = -3,
    WaitTimeout = -4,
    NotInitialized = -10,
    AlreadyInitialized = 10,
    NotLoaded = -11,
    AlreadyLoaded = 11,
    Insufficient = -20,
    PartiallyRegistered = 21,
    LicenseAbsent = -100,
    LicenseExpired = -101,
    LicenseRejected = -102,
    TooManyJobs = -201,
    InvalidJobId = -202,
    JobBusy = -203,
    NoMoreData = 204,
    OutOfMemory = -206,
    FileNotFound = -1001,
    PathNotFound = -1002,
    ReadFault = -1003,
    CountLimit = -1004,
    UserDicLocked = -1011,
    UserDicNoEntry = -1012,
}

/// <summary>AITalk のイベント理由コード。</summary>
internal enum AITalkEventReasonCode
{
    TextBufFull = 101,
    TextBufFlush = 102,
    TextBufClose = 103,
    RawBufFull = 201,
    RawBufFlush = 202,
    RawBufClose = 203,
    PhLabel = 301,
    Bookmark = 302,
    AutoBookmark = 303,
}

/// <summary>ジョブの入出力モード。</summary>
internal enum AITalkJobInOut
{
    PlainToWave = 11,
    AiKanaToWave = 12,
    JeitaToWave = 13,
    PlainToAiKana = 21,
    AiKanaToJeita = 32,
}

/// <summary>拡張フォーマット指定。</summary>
[Flags]
internal enum ExtendFormat
{
    None = 0,
    JeitaRuby = 1,
    AutoBookmark = 16,
}

// ---------------- コールバック関数ポインタ ----------------

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int AITalkProcTextBuf(AITalkEventReasonCode reasonCode, int jobId, IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int AITalkProcRawBuf(AITalkEventReasonCode reasonCode, int jobId, ulong tick, IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int AITalkProcEventTts(AITalkEventReasonCode reasonCode, int jobId, ulong tick, IntPtr name, IntPtr userData);

// ---------------- 構造体 (aitalk_AITalk.h の #pragma pack(1) に対応) ----------------

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AITalk_TConfig
{
    public uint hzVoiceDB;
    public IntPtr dirVoiceDBS;
    public uint msecTimeout;
    public IntPtr pathLicense;
    public IntPtr codeAuthSeed;
    public uint __reserved__;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AITalk_TJobParam
{
    public AITalkJobInOut modeInOut;
    public IntPtr userData;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AITalk_TJeitaParam
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string femaleName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string maleName;
    public int pauseMiddle;
    public int pauseLong;
    public int pauseSentence;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
    public string control;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AITalk_TSpeakerParam
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string voiceName;
    public float volume;
    public float speed;
    public float pitch;
    public float range;
    public int pauseMiddle;
    public int pauseLong;
    public int pauseSentence;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string styleRate;
}

/// <summary>
/// 可変長 (Speaker[1] 以降に追加スピーカーが続く) のため、本体は GetParam が返す
/// バッファに対して先頭固定部のみをマーシャリングして使う。aitalk_wrapper の
/// `reinterpret_cast&lt;AITalk_TTtsParam*&gt;(param_buffer.data())` と同じ扱い。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AITalk_TTtsParam
{
    public const int MaxVoiceName = 80;
    public const int MaxJeitaControl = 12;

    public uint size;
    public IntPtr procTextBuf;
    public IntPtr procRawBuf;
    public IntPtr procEventTts;
    public uint lenTextBufBytes;
    public uint lenRawBufBytes;
    public float volume;
    public int pauseBegin;
    public int pauseTerm;
    public ExtendFormat extendFormat;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string voiceName;
    public AITalk_TJeitaParam Jeita;
    public uint numSpeakers;
    public int __reserved__;
    public AITalk_TSpeakerParam Speaker;
}

// ---------------- aitalked.dll の関数ポインタ ----------------

internal delegate AITalkResultCode Fn_AITalkAPI_CloseKana(int jobId, int option);
internal delegate AITalkResultCode Fn_AITalkAPI_CloseSpeech(int jobId, int option);
internal delegate AITalkResultCode Fn_AITalkAPI_End();
internal delegate AITalkResultCode Fn_AITalkAPI_GetData(int jobId, [Out] short[] buffer, uint bufferSize, out uint readSamples);
internal delegate AITalkResultCode Fn_AITalkAPI_GetJeitaControl(int jobId, [Out] byte[] control);
internal delegate AITalkResultCode Fn_AITalkAPI_GetKana(int jobId, [Out] byte[] buffer, uint bufferSize, out uint readBytes, out uint pos);
internal delegate AITalkResultCode Fn_AITalkAPI_GetParam(IntPtr param, ref uint size);
internal delegate AITalkResultCode Fn_AITalkAPI_GetStatus(int jobId, out AITalkStatusCode status);
internal delegate AITalkResultCode Fn_AITalkAPI_Init(ref AITalk_TConfig config);
internal delegate AITalkResultCode Fn_AITalkAPI_LangClear();
internal delegate AITalkResultCode Fn_AITalkAPI_LangLoad([MarshalAs(UnmanagedType.LPStr)] string path);
internal delegate AITalkResultCode Fn_AITalkAPI_LicenseDate([Out] byte[] date);
internal delegate AITalkResultCode Fn_AITalkAPI_LicenseInfo([MarshalAs(UnmanagedType.LPStr)] string name, [Out] byte[] info, uint infoSize, out uint size);
internal delegate AITalkResultCode Fn_AITalkAPI_ModuleFlag();
internal delegate AITalkResultCode Fn_AITalkAPI_ReloadPhraseDic([MarshalAs(UnmanagedType.LPStr)] string? path);
internal delegate AITalkResultCode Fn_AITalkAPI_ReloadSymbolDic([MarshalAs(UnmanagedType.LPStr)] string? path);
internal delegate AITalkResultCode Fn_AITalkAPI_ReloadWordDic([MarshalAs(UnmanagedType.LPStr)] string? path);
internal delegate AITalkResultCode Fn_AITalkAPI_SetParam(ref AITalk_TTtsParam param);
internal delegate AITalkResultCode Fn_AITalkAPI_TextToKana(ref int jobId, ref AITalk_TJobParam jobParam, [MarshalAs(UnmanagedType.LPStr)] string text);
internal delegate AITalkResultCode Fn_AITalkAPI_TextToSpeech(ref int jobId, ref AITalk_TJobParam jobParam, [MarshalAs(UnmanagedType.LPStr)] string kana);
internal delegate AITalkResultCode Fn_AITalkAPI_VersionInfo(int moduleId, [Out] byte[] version, uint versionSize, out uint size);
internal delegate AITalkResultCode Fn_AITalkAPI_VoiceClear();
internal delegate AITalkResultCode Fn_AITalkAPI_VoiceLoad([MarshalAs(UnmanagedType.LPStr)] string voiceName);

internal enum AITalkStatusCode
{
    WrongState = -1,
    InProgress = 10,
    StillRunning = 11,
    Done = 12,
}

/// <summary>
/// kernel32 相当のロード・解決。Windows のバージョンによって LoadLibrary / GetProcAddress の
/// エクスポート名 (無装飾 / A / W サフィックス) が異なるため、P/Invoke の名前解決に頼らず
/// .NET 標準の <see cref="NativeLibrary"/> で動的解決する。
/// </summary>
internal static class NativeMethods
{
    public static IntPtr LoadLibrary(string path)
        => NativeLibrary.Load(path);

    public static IntPtr GetProcAddress(IntPtr module, string name)
        => NativeLibrary.TryGetExport(module, name, out IntPtr address) ? address : IntPtr.Zero;

    public static void FreeLibrary(IntPtr module)
    {
        if (module != IntPtr.Zero)
            NativeLibrary.Free(module);
    }
}
