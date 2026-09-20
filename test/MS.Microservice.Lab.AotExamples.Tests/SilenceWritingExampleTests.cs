using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.SilenceWritingExample;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.SilenceWritingExample;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class SilenceWritingExampleTests
{
    [Theory]
    [InlineData(44100, 16, 2, 0f)]
    [InlineData(44100, 16, 2, 0.00002f)]
    [InlineData(8000, 16, 1, 0.256f)]
    [InlineData(8000, 16, 1, 0.256125f)]
    [InlineData(48000, 24, 2, 0.5f)]
    [InlineData(44100, 16, 2, 1f)]
    public void Versions_ProduceIdenticalBytesWithDifferentWriteSizes(int rate, int bits, int channels, float seconds)
    {
        using var legacy = new ObservedStream();
        using var current = new ObservedStream();

        LegacyExample.Write(legacy, rate, bits, channels, seconds);
        StaticExample.Write(current, rate, bits, channels, seconds);

        Assert.Equal(legacy.ToArray(), current.ToArray());
        Assert.InRange(current.LargestWrite, 0, 4096);
        Assert.Equal(legacy.Length, legacy.LargestWrite);
        if (legacy.Length > 4096) Assert.True(legacy.LargestWrite > current.LargestWrite);
    }

    [Fact]
    public void StaticVersion_ReusesItsBufferWithoutMixingAdjacentAudioBytes()
    {
        using var legacy = new MemoryStream();
        using var current = new ObservedStream();
        foreach (byte marker in new byte[] { 11, 22, 33 })
        {
            legacy.WriteByte(marker);
            current.WriteByte(marker);
            LegacyExample.Write(legacy, 8000, 16, 1, 0.256125f);
            var previous = current.FirstBuffer;
            StaticExample.Write(current, 8000, 16, 1, 0.256125f);
            if (previous is not null) Assert.Same(previous, current.FirstBuffer);
        }

        Assert.Equal(legacy.ToArray(), current.ToArray());
        Assert.All(current.FirstBuffer!, value => Assert.Equal(0, value));

        using var another = new ObservedStream();
        StaticExample.Write(another, 8000, 16, 1, 0.125f);
        Assert.Same(current.FirstBuffer, another.FirstBuffer);
    }

    private sealed class ObservedStream : MemoryStream
    {
        public int LargestWrite { get; private set; }
        public byte[]? FirstBuffer { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            FirstBuffer ??= buffer;
            LargestWrite = Math.Max(LargestWrite, count);
            base.Write(buffer, offset, count);
        }
    }
}
