using System.Security.Cryptography;
using System.Text;
using MS.Microservice.Core.IO;

namespace MS.Microservice.Core.Tests.IO;

public sealed class FileHashHelperTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ms-file-hash-{Guid.NewGuid():N}");

    public FileHashHelperTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ComputeSha256Async_File_ReturnsKnownDigestAndReleasesFile()
    {
        var path = WriteFile("中文 文件.txt", "abc"u8.ToArray());

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", await FileHashHelper.ComputeSha256Async(path));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(3, exclusive.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task ComputeSha256Async_Stream_HashesRemainingBytesAndLeavesOpen(int offset)
    {
        var bytes = "prefix:你好，stream"u8.ToArray();
        using var stream = new MemoryStream(bytes) { Position = offset };

        var actual = await FileHashHelper.ComputeSha256Async(stream);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset))), actual);
        Assert.True(stream.CanRead);
        Assert.Equal(bytes.Length, stream.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_003)]
    public async Task ComputeSha256Async_NonSeekableShortReads_HashesEveryByte(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
        using var stream = new ChunkedStream(bytes, 3);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), await FileHashHelper.ComputeSha256Async(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ComputeSha256Async_PreCanceled_DoesNotReadStream()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var stream = new ChunkedStream([1, 2, 3], 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHashHelper.ComputeSha256Async(stream, source.Token));

        Assert.Equal(0, stream.ReadCount);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ComputeSha256Async_CanceledDuringRead_PropagatesAndLeavesStreamOpen()
    {
        using var source = new CancellationTokenSource();
        using var stream = new ChunkedStream([1, 2, 3], 1, source.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHashHelper.ComputeSha256Async(stream, source.Token));

        Assert.True(stream.CanRead);
        Assert.Equal(1, stream.ReadCount);
    }

    [Fact]
    public async Task ComputeSha256Async_UnreadableStream_IsRejected()
    {
        var path = WriteFile("write-only", [1]);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);

        await Assert.ThrowsAsync<ArgumentException>(() => FileHashHelper.ComputeSha256Async(stream));
    }

    [Fact]
    public async Task ComputeSha256Async_MissingFile_Fails()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileHashHelper.ComputeSha256Async(Path.Combine(_directory, "missing")));
    }

    [Fact]
    public async Task FindDuplicatesAsync_EmptyAndSingleFiles_ReturnNoGroups()
    {
        Assert.Empty(await FileHashHelper.FindDuplicatesAsync([]));
        Assert.Empty(await FileHashHelper.FindDuplicatesAsync([WriteFile("single", [1, 2])]));
    }

    [Fact]
    public async Task FindDuplicatesAsync_DifferentLengthAndSameLengthDifferentBytes_AreExcluded()
    {
        var paths = new[] { WriteFile("a", [1, 2]), WriteFile("b", [1, 3]), WriteFile("c", [1, 2, 3]) };

        Assert.Empty(await FileHashHelper.FindDuplicatesAsync(paths));
    }

    [Fact]
    public async Task FindDuplicatesAsync_MultipleGroupsAndEmptyFiles_AreDeterministic()
    {
        var a = WriteFile("a-empty", []);
        var b = WriteFile("b-empty", []);
        var c = WriteFile("c-content", "内容"u8.ToArray());
        var d = WriteFile("d-content", "内容"u8.ToArray());
        var e = WriteFile("e-content", "内容"u8.ToArray());
        var unique = WriteFile("f-other", "不同"u8.ToArray());
        var paths = new[] { e, unique, b, c, a, d };

        var groups = await FileHashHelper.FindDuplicatesAsync(paths);
        var reversed = await FileHashHelper.FindDuplicatesAsync(paths.Reverse());

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { a, b }, groups[0].Paths);
        Assert.Equal(0, groups[0].Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])), groups[0].Sha256);
        Assert.Equal(new[] { c, d, e }, groups[1].Paths);
        Assert.Equal(Encoding.UTF8.GetByteCount("内容"), groups[1].Length);
        Assert.Equal(groups.Select(group => group.Sha256), reversed.Select(group => group.Sha256));
        Assert.Equal(groups.SelectMany(group => group.Paths), reversed.SelectMany(group => group.Paths));
    }

    [Fact]
    public async Task FindDuplicatesAsync_RepeatedAndRelativeAliases_DoNotBecomeDuplicates()
    {
        var path = WriteFile("same.txt", [1]);
        var alias = Path.Combine(_directory, ".", "same.txt");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);

        Assert.Empty(await FileHashHelper.FindDuplicatesAsync([path, alias, relative, path]));
    }

    [Fact]
    public async Task FindDuplicatesAsync_PathCase_FollowsPlatformPolicy()
    {
        var lower = WriteFile("case.txt", [5]);
        var upper = Path.Combine(_directory, "CASE.TXT");

        if (OperatingSystem.IsWindows())
        {
            var other = WriteFile("other.txt", [5]);
            var forward = Assert.Single(await FileHashHelper.FindDuplicatesAsync([lower, upper, other]));
            var reverse = Assert.Single(await FileHashHelper.FindDuplicatesAsync([other, upper, lower]));
            Assert.Equal(2, forward.Paths.Count);
            Assert.Equal(forward.Paths, reverse.Paths);
        }
        else
        {
            WriteFile("CASE.TXT", [5]);
            Assert.Equal(2, Assert.Single(await FileHashHelper.FindDuplicatesAsync([lower, upper])).Paths.Count);
        }
    }

    [Fact]
    public async Task FindDuplicatesAsync_MissingSingleton_FailsInsteadOfSkippingValidation()
    {
        var existing = WriteFile("existing", [1, 2]);

        await Assert.ThrowsAsync<FileNotFoundException>(() => FileHashHelper.FindDuplicatesAsync([existing, Path.Combine(_directory, "missing")]));
    }

    [Fact]
    public async Task FindDuplicatesAsync_Directory_IsRejected()
    {
        await Assert.ThrowsAnyAsync<IOException>(() => FileHashHelper.FindDuplicatesAsync([_directory]));
    }

    [Fact]
    public async Task FindDuplicatesAsync_LargeFiles_AreHashedToSameGroup()
    {
        var bytes = new byte[2_000_003];
        Random.Shared.NextBytes(bytes);
        var first = WriteFile("large-a", bytes);
        var second = WriteFile("large-b", bytes);
        bytes[^1] ^= 1;
        var different = WriteFile("large-different", bytes);

        var group = Assert.Single(await FileHashHelper.FindDuplicatesAsync([different, second, first]));

        Assert.Equal(new[] { first, second }, group.Paths);
        Assert.Equal(bytes.Length, group.Length);
    }

    [Fact]
    public async Task FindDuplicatesAsync_PreCanceled_DoesNotEnumerate()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHashHelper.FindDuplicatesAsync(FailIfEnumerated(), source.Token));
    }

    [Fact]
    public async Task FindDuplicatesAsync_CanceledDuringEnumeration_Stops()
    {
        using var source = new CancellationTokenSource();
        var path = WriteFile("cancel", [1]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHashHelper.FindDuplicatesAsync(CancelAfterFirst(), source.Token));

        IEnumerable<string> CancelAfterFirst()
        {
            yield return path;
            source.Cancel();
            yield return path;
            throw new InvalidOperationException("Cancellation was ignored.");
        }
    }

    [Fact]
    public async Task FindDuplicatesAsync_ResultsCannotBeMutatedThroughCollectionInterfaces()
    {
        var first = WriteFile("a", [7]);
        var second = WriteFile("b", [7]);
        var groups = await FileHashHelper.FindDuplicatesAsync([first, second]);

        Assert.Throws<NotSupportedException>(() => ((IList<FileDuplicateGroup>)groups).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)groups[0].Paths).Clear());
    }

    [Fact]
    public async Task NullAndBlankInputs_AreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => FileHashHelper.ComputeSha256Async((Stream)null!));
        await Assert.ThrowsAsync<ArgumentException>(() => FileHashHelper.ComputeSha256Async(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FileHashHelper.FindDuplicatesAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => FileHashHelper.FindDuplicatesAsync([""]));
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static IEnumerable<string> FailIfEnumerated() => Enumerable.Repeat("unused", 1)
        .Select<string, string>(_ => throw new InvalidOperationException("The source should not be enumerated."));

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class ChunkedStream(byte[] bytes, int chunkSize, Action? afterRead = null) : Stream
    {
        private int _position;
        private bool _disposed;

        public int ReadCount { get; private set; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - _position);
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            ReadCount++;
            afterRead?.Invoke();
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
