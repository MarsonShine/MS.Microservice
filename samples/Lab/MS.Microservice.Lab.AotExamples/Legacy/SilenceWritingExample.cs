namespace MS.Microservice.Lab.AotExamples.Legacy;

/// <summary>保留原 WAV/MP3 静音写入算法，用标量参数表示音频格式。</summary>
public static class SilenceWritingExample
{
    public static void Write(Stream output, int sampleRate, int bitsPerSample, int channels, float seconds)
    {
        int bytesPerSample = bitsPerSample / 8;
        int samplesPerSecond = sampleRate * channels;
        int totalSamples = (int)(samplesPerSecond * seconds);
        var silenceBuffer = new byte[totalSamples * bytesPerSample];
        output.Write(silenceBuffer, 0, silenceBuffer.Length);
    }
}
