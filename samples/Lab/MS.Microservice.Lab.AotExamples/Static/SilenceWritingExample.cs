namespace MS.Microservice.Lab.AotExamples.Static;

/// <summary>共享私有零块，各输出流独立按所需长度写入；格式参数不依赖音频设备。</summary>
public static class SilenceWritingExample
{
    private static readonly byte[] SilenceBuffer = new byte[4096];

    public static void Write(Stream output, int sampleRate, int bitsPerSample, int channels, float seconds)
    {
        int bytesPerSample = bitsPerSample / 8;
        int samplesPerSecond = checked(sampleRate * channels);
        int totalSamples = checked((int)(samplesPerSecond * seconds));
        int remaining = checked(totalSamples * bytesPerSample);
        ArgumentOutOfRangeException.ThrowIfNegative(remaining, nameof(seconds));
        if (remaining == 0) return;

        while (remaining > 0)
        {
            int count = Math.Min(remaining, SilenceBuffer.Length);
            output.Write(SilenceBuffer, 0, count);
            remaining -= count;
        }
    }
}
