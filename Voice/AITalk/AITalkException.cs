namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// AITalk SDK 操作中の例外。aitalked.dll 由来の結果コードを持つ場合は <see cref="Result"/> に格納する。
/// </summary>
internal sealed class AITalkException : Exception
{
    /// <summary>aitalked.dll が返した結果コード (該当しない場合は null)。</summary>
    public AITalkResultCode? Result { get; }

    public AITalkException(string message) : base(message)
    {
    }

    public AITalkException(string message, AITalkResultCode result)
        : base($"{message} (result: {result})")
    {
        Result = result;
    }
}
