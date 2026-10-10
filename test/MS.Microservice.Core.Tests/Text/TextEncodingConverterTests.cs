using System.Text;
using MS.Microservice.Core.Text;

namespace MS.Microservice.Core.Tests.Text;

public sealed class TextEncodingConverterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20_000)]
    public async Task Convert_UnicodeAcrossBuffersAndShortNonseekableReads_PreservesText(int repetitions)
    {
        var text = string.Concat(Enumerable.Repeat("中文🙂é\r\n", repetitions));
        var sourceEncoding = new UTF8Encoding(false, true);
        var targetEncoding = new UnicodeEncoding(false, true, true);
        using var source = new ChunkedReadStream(sourceEncoding.GetBytes(text), chunkSize: 3);
        using var destination = new MemoryStream();

        await TextEncodingConverter.ConvertAsync(source, destination, sourceEncoding, targetEncoding);

        Assert.Equal(new byte[] { 0xFF, 0xFE }.Concat(targetEncoding.GetBytes(text)), destination.ToArray());
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Convert_ConsumesMatchingSourcePreambleAndFollowsTargetPreamble(bool emitTargetPreamble)
    {
        var sourceEncoding = new UTF8Encoding(true, true);
        var targetEncoding = new UnicodeEncoding(true, emitTargetPreamble, true);
        const string text = "正文🙂\n";
        using var source = new ChunkedReadStream([.. sourceEncoding.GetPreamble(), .. sourceEncoding.GetBytes(text)], 1);
        using var destination = new MemoryStream();

        await TextEncodingConverter.ConvertAsync(source, destination, sourceEncoding, targetEncoding);

        Assert.Equal(targetEncoding.GetPreamble().Concat(targetEncoding.GetBytes(text)), destination.ToArray());
    }

    [Fact]
    public async Task Convert_SourceWithNoPreambleConfiguredPreservesLeadingBomAsText()
    {
        using var source = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'a']);
        using var destination = new MemoryStream();
        var encoding = new UTF8Encoding(false, true);

        await TextEncodingConverter.ConvertAsync(source, destination, encoding, encoding);

        Assert.Equal("\uFEFFa", encoding.GetString(destination.ToArray()));
    }

    [Fact]
    public async Task Convert_DoesNotAutodetectDifferentEncodingFromBom()
    {
        using var source = new MemoryStream([0xFF, 0xFE, 0x61, 0x00]);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<DecoderFallbackException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, new UTF8Encoding(false, true), new UTF8Encoding(false)));

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_UsesCurrentPositionsAndDoesNotInsertPreambleAfterExistingBytes()
    {
        using var source = new MemoryStream("skip:中文"u8.ToArray()) { Position = 5 };
        using var destination = new MemoryStream();
        destination.Write("prefix:"u8);

        await TextEncodingConverter.ConvertAsync(source, destination, new UTF8Encoding(false, true), Encoding.UTF8);

        Assert.Equal("prefix:中文", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Fact]
    public async Task Convert_StrictSourceRejectsTruncatedMultibyteCharacter()
    {
        using var source = new ChunkedReadStream([0xE4, 0xB8], chunkSize: 1);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<DecoderFallbackException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, new UTF8Encoding(false, true), new UnicodeEncoding(false, false, true)));

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Convert_TargetFallbackPolicyIsRespected(bool strict)
    {
        using var source = new MemoryStream("café"u8.ToArray());
        using var destination = new MemoryStream();
        var target = strict
            ? Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            : Encoding.ASCII;

        if (strict)
        {
            await Assert.ThrowsAsync<EncoderFallbackException>(() => TextEncodingConverter.ConvertAsync(
                source, destination, new UTF8Encoding(false, true), target));
        }
        else
        {
            await TextEncodingConverter.ConvertAsync(source, destination, new UTF8Encoding(false, true), target);
            Assert.Equal("caf?", target.GetString(destination.ToArray()));
        }

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_PreCancelledDoesNotReadOrWrite()
    {
        using var source = new ChunkedReadStream("abc"u8.ToArray(), 1);
        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, Encoding.UTF8, Encoding.UTF8, new CancellationToken(true)));

        Assert.Equal(0, source.ReadCalls);
        Assert.Empty(destination.ToArray());
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_CancellationDuringReadRemainsPrimaryWhenCleanupWriteFails()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new ChunkedReadStream("abc"u8.ToArray(), 1) { AfterRead = cancellation.Cancel };
        using var destination = new FailingWriteStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, Encoding.UTF8, Encoding.UTF8, cancellation.Token));

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_DecoderFailureRemainsPrimaryWhenCleanupWriteFails()
    {
        using var source = new ChunkedReadStream([0xFF], 1);
        using var destination = new FailingWriteStream();

        await Assert.ThrowsAsync<DecoderFallbackException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, new UTF8Encoding(false, true), Encoding.UTF8));

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_OutputFailurePropagatesWithoutClosingStreams()
    {
        using var source = new MemoryStream("abc"u8.ToArray());
        using var destination = new FailingWriteStream();

        await Assert.ThrowsAsync<IOException>(() => TextEncodingConverter.ConvertAsync(
            source, destination, Encoding.UTF8, Encoding.UTF8));

        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task Convert_SameStreamIsRejectedWithoutChangingItsContent()
    {
        using var stream = new MemoryStream("abc"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() => TextEncodingConverter.ConvertAsync(
            stream, stream, Encoding.UTF8, Encoding.Unicode));

        Assert.Equal("abc"u8.ToArray(), stream.ToArray());
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Convert_RejectsUnreadableOrUnwritableStreams()
    {
        using var disposed = new MemoryStream();
        disposed.Dispose();
        using var readable = new MemoryStream([1], writable: false);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => TextEncodingConverter.ConvertAsync(
            disposed, output, Encoding.UTF8, Encoding.UTF8));
        await Assert.ThrowsAsync<ArgumentException>(() => TextEncodingConverter.ConvertAsync(
            output, readable, Encoding.UTF8, Encoding.UTF8));
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("output failed"));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException(new IOException("output failed"));
        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.FromException(new IOException("output flush failed"));
    }

    private sealed class ChunkedReadStream(byte[] bytes, int chunkSize) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public Action? AfterRead { get; init; }
        public int ReadCalls { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, chunkSize));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            var read = await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
            AfterRead?.Invoke();
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
