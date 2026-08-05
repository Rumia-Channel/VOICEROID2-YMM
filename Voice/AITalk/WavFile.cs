namespace Voiceroid2Ymm.Voice.AITalk;

/// <summary>
/// 16 bit PCM モノラル WAV ファイルの書き出し。NAudio 等の依存なしで完結させる。
/// </summary>
public static class WavFile
{
    /// <summary>
    /// 16 bit PCM のサンプル列を WAV ファイルとして書き出す。
    /// <paramref name="samples"/> が空でもヘッダのみの有効な WAV を作成する。
    /// </summary>
    public static void WritePcm16Mono(string filePath, ReadOnlySpan<short> samples, int sampleRate = 44100)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        int dataSize = checked(samples.Length * sizeof(short));
        int byteRate = checked(sampleRate * sizeof(short));

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);   // PCM
        writer.Write((short)1);   // モノラル
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)sizeof(short));
        writer.Write((short)16);  // 16 bit
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (short sample in samples)
            writer.Write(sample);
    }
}
