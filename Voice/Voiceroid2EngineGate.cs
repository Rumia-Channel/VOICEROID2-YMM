namespace Voiceroid2Ymm.Voice;

/// <summary>
/// aitalked.dll (AITalk SDK) へのアクセスをプロセス単位で 1 つに絞るための共有ゲート。
/// 複数話者の同時合成要求を直列化する (aitalked.dll は同時ジョブに制限があるため)。
/// VoicePeak-plus の VoicePeakPipeGate と同じ設計。
/// </summary>
internal static class Voiceroid2EngineGate
{
    public static SemaphoreSlim Semaphore { get; } = new(1, 1);
}
