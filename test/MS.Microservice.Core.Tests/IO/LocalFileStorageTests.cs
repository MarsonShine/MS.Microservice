using MS.Microservice.Core.IO;
using Xunit;

namespace MS.Microservice.Core.Tests.IO;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "MS.Microservice.LocalFileStorageTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(".png", ".png")]
    [InlineData(".JPEG", ".jpeg")]
    [InlineData(".tmp", ".tmp")]
    [InlineData(".123456789", ".123456789")]
    public async Task Save_PublishesCompleteContentUnderNormalizedRandomName(string extension, string expected)
    {
        byte[] bytes = [1, 2, 3, 4];
        using var input = new MemoryStream(bytes);
        var fileName = await new LocalFileStorage(_root).SaveAsync(input, extension);

        Assert.EndsWith(expected, fileName, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, fileName)));
        Assert.Equal(fileName, Path.GetFileName(Assert.Single(Directory.GetFiles(_root))));
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("png")]
    [InlineData("../png")]
    [InlineData(".a/b")]
    [InlineData(".a\\b")]
    [InlineData(".a:b")]
    [InlineData(".中")]
    [InlineData(".png ")]
    [InlineData(".1234567890")]
    public async Task Save_RejectsInvalidSuffixWithoutCreatingOutput(string extension)
    {
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<ArgumentException>(
            () => new LocalFileStorage(_root).SaveAsync(input, extension));
        Assert.False(Directory.Exists(_root));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Save_ReadsFromCurrentPositionAndAcceptsEmptyContent()
    {
        using var input = new MemoryStream([1, 2, 3]);
        input.Position = 2;
        var storage = new LocalFileStorage(_root);
        var suffix = await storage.SaveAsync(input, ".bin");
        var empty = await storage.SaveAsync(input, ".bin");

        Assert.Equal(new byte[] { 3 }, await File.ReadAllBytesAsync(Path.Combine(_root, suffix)));
        Assert.Empty(await File.ReadAllBytesAsync(Path.Combine(_root, empty)));
        Assert.NotEqual(suffix, empty);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Save_CopiesReadableNonseekableStream()
    {
        await using var input = new NonseekableStream([4, 5, 6]);
        var fileName = await new LocalFileStorage(_root).SaveAsync(input, ".bin");
        Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(Path.Combine(_root, fileName)));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Save_WhenCopyFails_RemovesOnlyOwnedTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "existing.txt");
        await File.WriteAllTextAsync(existing, "keep");
        using var input = new CallbackCopyStream(() => throw new IOException("copy failed"));

        var error = await Assert.ThrowsAsync<IOException>(
            () => new LocalFileStorage(_root).SaveAsync(input, ".png"));

        Assert.Equal("copy failed", error.Message);
        Assert.Equal(existing, Assert.Single(Directory.GetFiles(_root)));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Save_DoesNotPublishUntilCopyCompletes()
    {
        using var input = new CallbackCopyStream(() =>
        {
            var temporary = Assert.Single(Directory.GetFiles(_root));
            Assert.StartsWith(".", Path.GetFileName(temporary));
            Assert.DoesNotContain(Directory.GetFiles(_root), path => path.EndsWith(".png", StringComparison.Ordinal));
        });

        var fileName = await new LocalFileStorage(_root).SaveAsync(input, ".png");
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(Path.Combine(_root, fileName)));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Save_WhenCancelledAfterCopy_DoesNotPublish()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CallbackCopyStream(cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new LocalFileStorage(_root).SaveAsync(input, ".png", cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Save_WhenPreCancelled_DoesNotCreateDirectory()
    {
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new LocalFileStorage(_root).SaveAsync(input, ".png", new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Save_RejectsUnreadableInput()
    {
        using var input = new MemoryStream([1]);
        input.Dispose();
        await Assert.ThrowsAsync<ArgumentException>(
            () => new LocalFileStorage(_root).SaveAsync(input, ".png"));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Save_ConcurrentCallsUseDistinctFiles()
    {
        var storage = new LocalFileStorage(_root);
        var tasks = Enumerable.Range(0, 8).Select(async value =>
        {
            using var input = new MemoryStream([(byte)value]);
            var name = await storage.SaveAsync(input, ".bin");
            Assert.Equal(new[] { (byte)value }, await File.ReadAllBytesAsync(Path.Combine(_root, name)));
            return name;
        });

        var names = await Task.WhenAll(tasks);
        Assert.Equal(8, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, Directory.GetFiles(_root).Length);
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.LocalFileStorageTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class CallbackCopyStream(Action callback) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1, 2 }, cancellationToken);
            callback();
        }
    }

    private sealed class NonseekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 1));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
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
