using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Lame;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace MS.Microservice.Audio.Tests;

public sealed class SilenceBufferTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1f)]
    [InlineData(10f)]
    public void SilenceWrite_DoesNotSubmitAFullDurationArray(float seconds)
    {
        using var destination = new RecordingStream();

        AudioProcessor.WriteSilence(destination, new WaveFormat(44100, 16, 2), seconds);

        Assert.InRange(destination.LargestWrite, 1, 4096);
        Assert.Equal((long)(176400 * seconds), destination.BytesWritten);
        Assert.True(destination.AllZero);
    }

    [Theory]
    [InlineData(8000, 16, 1, 0f, 0)]
    [InlineData(44100, 16, 2, 0.00001f, 0)]
    [InlineData(44100, 16, 2, 0.00002f, 2)]
    [InlineData(8000, 16, 1, 0.256f, 4096)]
    [InlineData(8000, 16, 1, 0.256125f, 4098)]
    [InlineData(48000, 24, 2, 0.5f, 144000)]
    [InlineData(48000, 32, 2, 120f, 46080000)]
    public void SilenceWrite_PreservesByteCountRoundingAndZeroContent(int rate, int bits, int channels, float seconds, long expectedBytes)
    {
        using var destination = new RecordingStream();

        AudioProcessor.WriteSilence(destination, new WaveFormat(rate, bits, channels), seconds);

        Assert.Equal(expectedBytes, destination.BytesWritten);
        Assert.True(destination.AllZero);
        Assert.True(destination.SameBuffer);
        Assert.InRange(destination.LargestWrite, 0, 4096);
        if (expectedBytes == 0) Assert.Null(destination.FirstBuffer);
        else Assert.Equal((int)((expectedBytes - 1) % 4096 + 1), destination.LastWrite);
    }

    [Fact]
    public void RepeatedSilence_ReusesOneSmallBufferWithoutPerSegmentAllocations()
    {
        var format = new WaveFormat(44100, 16, 2);
        using var warmup = new RecordingStream();
        AudioProcessor.WriteSilence(warmup, format, 1f);
        using var destination = new RecordingStream();
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++) AudioProcessor.WriteSilence(destination, format, 1f);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"100 one-second segments: {allocated} scratch-path bytes allocated.");
        Assert.True(allocated < 1024, $"Allocated {allocated} bytes for repeated silence.");
        Assert.Equal(17_640_000, destination.BytesWritten);
        Assert.True(destination.SameBuffer);
        Assert.Same(warmup.FirstBuffer, destination.FirstBuffer);
        Assert.True(destination.AllZero);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.125f)]
    [InlineData(1f)]
    public async Task WavCombine_PreservesAudioAndSilenceAfterEverySourceIncludingTheLast(float seconds)
    {
        var format = new WaveFormat(8000, 16, 1);
        byte[] first = [1, 2, 3, 4];
        byte[] second = [5, 6, 7, 8];
        var silence = new byte[(int)(8000 * seconds) * 2];
        byte[] expected = [.. first, .. silence, .. second, .. silence];
        var processor = new AudioProcessor();

        var combined = await processor.CombineAudioDataAsync(
            [Wav(first, format), Wav(second, format)], AudioFormat.Wav,
            new AudioCombineOptions { TargetFormat = format, SilenceDuration = seconds });

        using var reader = new WaveFileReader(new MemoryStream(combined));
        var actual = new byte[checked((int)reader.Length)];
        reader.ReadExactly(actual);
        Assert.Equal(expected, actual);
        Assert.Equal(format, reader.WaveFormat);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.75f)]
    [InlineData(1.5f)]
    public void Mp3Chunks_ProduceTheSameEncodedBytesAsOneWholeBuffer(float seconds)
    {
        var format = new WaveFormat(44100, 16, 2);
        using var whole = new MemoryStream();
        using (var writer = new LameMP3FileWriter(whole, format, LAMEPreset.STANDARD))
        {
            var bytes = new byte[(int)(44100 * 2 * seconds) * 2];
            writer.Write(bytes, 0, bytes.Length);
        }
        using var chunked = new MemoryStream();
        using (var writer = new LameMP3FileWriter(chunked, format, LAMEPreset.STANDARD))
        {
            AudioProcessor.WriteSilence(writer, format, seconds);
        }

        Assert.NotEmpty(whole.ToArray());
        Assert.Equal(whole.ToArray(), chunked.ToArray());
    }

    [Fact]
    public void WriteFailure_IsPropagatedWithoutDisposingTheCallersStream()
    {
        using var destination = new RecordingStream { FailOnWrite = 2 };

        Assert.Same(destination.Failure, Assert.Throws<IOException>(() =>
            AudioProcessor.WriteSilence(destination, new WaveFormat(44100, 16, 2), 1f)));
        Assert.Equal(4096, destination.BytesWritten);
        Assert.False(destination.Disposed);
    }

    [Theory]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    public void UnrepresentableLength_FailsBeforeWriting(float seconds)
    {
        using var destination = new RecordingStream();

        Assert.Throws<OverflowException>(() =>
            AudioProcessor.WriteSilence(destination, new WaveFormat(44100, 16, 2), seconds));
        Assert.Equal(0, destination.BytesWritten);
        Assert.Null(destination.FirstBuffer);
    }

    [Fact]
    public async Task ConcurrentOutputs_ShareOnlyZeroDataAndKeepIndependentLengths()
    {
        var streams = await Task.WhenAll(Enumerable.Range(1, 16).Select(index => Task.Run(() =>
        {
            var stream = new RecordingStream();
            AudioProcessor.WriteSilence(stream, new WaveFormat(8000, 16, 1), index / 8f);
            return stream;
        })));
        try
        {
            for (int i = 0; i < streams.Length; i++)
            {
                Assert.Equal((i + 1) * 2000, streams[i].BytesWritten);
                Assert.True(streams[i].AllZero);
                Assert.True(streams[i].SameBuffer);
                Assert.Same(streams[0].FirstBuffer, streams[i].FirstBuffer);
            }
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static byte[] Wav(byte[] pcm, WaveFormat format)
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, format)) writer.Write(pcm, 0, pcm.Length);
        return stream.ToArray();
    }

    private sealed class RecordingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public int LargestWrite { get; private set; }
        public int LastWrite { get; private set; }
        public bool AllZero { get; private set; } = true;
        public bool SameBuffer { get; private set; } = true;
        public byte[]? FirstBuffer { get; private set; }
        public int FailOnWrite { get; init; }
        public IOException Failure { get; } = new("write failed");
        public bool Disposed { get; private set; }
        private int writes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !Disposed;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (++writes == FailOnWrite) throw Failure;
            FirstBuffer ??= buffer;
            SameBuffer &= ReferenceEquals(FirstBuffer, buffer);
            AllZero &= buffer.AsSpan(offset, count).IndexOfAnyExcept((byte)0) < 0;
            BytesWritten += count;
            LargestWrite = Math.Max(LargestWrite, count);
            LastWrite = count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
