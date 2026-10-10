using System.IO.Compression;
using System.Text;
using MS.Microservice.Core.IO;

namespace MS.Microservice.Core.Tests.IO;

public sealed class ZipArchiveHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zip-tests-" + Guid.NewGuid().ToString("N"));

    public ZipArchiveHelperTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RoundTripsTreeEmptyDirectoriesUnicodeAndLargeContentWithoutClosingStreams()
    {
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(Path.Combine(input, "资料", "empty"));
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(input, "资料", "binary.dat"), bytes);
        await File.WriteAllTextAsync(Path.Combine(input, "hello.txt"), "你好 ZIP");
        using var zip = new MemoryStream();

        await ZipArchiveHelper.CreateZipAsync(input, zip, CompressionLevel.Fastest);
        Assert.True(zip.CanWrite);
        zip.Position = 0;
        var output = Path.Combine(_root, "output");
        await ZipArchiveHelper.ExtractZipAsync(zip, output);

        Assert.True(zip.CanRead);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(output, "资料", "binary.dat")));
        Assert.Equal("你好 ZIP", await File.ReadAllTextAsync(Path.Combine(output, "hello.txt")));
        Assert.True(Directory.Exists(Path.Combine(output, "资料", "empty")));
        AssertNoStaging();
    }

    [Fact]
    public async Task RoundTripsEmptyRootWithZeroEntryAndByteLimits()
    {
        var input = Directory.CreateDirectory(Path.Combine(_root, "input"));
        using var zip = new MemoryStream();
        await ZipArchiveHelper.CreateZipAsync(input.FullName, zip);
        zip.Position = 0;
        await ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output"),
            new ZipExtractionOptions { MaxEntries = 0, MaxEntryBytes = 0, MaxTotalBytes = 0 });
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "output")));
    }

    [Fact]
    public async Task SupportsBackslashEntriesAndExplicitDirectoryAfterItsContents()
    {
        using var zip = CreateArchive(("nested\\file.txt", "content"), ("nested/", ""));
        await ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output"));
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(_root, "output", "nested", "file.txt")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/../../outside")]
    [InlineData("nested\\..\\outside")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    [InlineData("C:/absolute")]
    [InlineData("C:relative")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("file:stream")]
    [InlineData("nested//file")]
    [InlineData("./file")]
    [InlineData("trailing. /file")]
    [InlineData("trailing./file")]
    [InlineData("NUL.txt")]
    [InlineData("CON .txt")]
    [InlineData("NUL .txt")]
    [InlineData("COM1 .txt")]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    [InlineData(" leading-space")]
    [InlineData("com1")]
    [InlineData("LPT¹.txt")]
    [InlineData("bad?name")]
    [InlineData("bad\u0001name")]
    public async Task RejectsNonportableAndEscapingNamesWithoutPublishing(string name)
    {
        using var zip = CreateArchive(("safe.txt", "safe"), (name, "bad"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output")));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        Assert.Equal([], Directory.GetFiles(_root));
        AssertNoStaging();
    }

    [Theory]
    [InlineData("file", "file")]
    [InlineData("a/file", "a\\file")]
    [InlineData("a/", "a/")]
    [InlineData("a", "a/file")]
    [InlineData("a/file", "a")]
    [InlineData("a", "a/")]
    [InlineData("a/", "a")]
    public async Task RejectsDuplicateAndConflictingPaths(string first, string second)
    {
        using var zip = CreateArchive((first, ""), (second, ""));
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output")));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task CaseAliasesFollowOperatingSystemPathSemantics()
    {
        using var zip = CreateArchive(("file", "lower"), ("FILE", "upper"));
        var output = Path.Combine(_root, "output");
        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, output));
            Assert.False(Directory.Exists(output));
        }
        else
        {
            await ZipArchiveHelper.ExtractZipAsync(zip, output);
            Assert.Equal("lower", await File.ReadAllTextAsync(Path.Combine(output, "file")));
            Assert.Equal("upper", await File.ReadAllTextAsync(Path.Combine(output, "FILE")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeverOverwritesAnExistingDestination(bool directory)
    {
        var output = Path.Combine(_root, "output");
        if (directory)
            Directory.CreateDirectory(output);
        else
            await File.WriteAllTextAsync(output, "original");
        using var zip = CreateArchive(("file", "new"));

        await Assert.ThrowsAsync<IOException>(() => ZipArchiveHelper.ExtractZipAsync(zip, output));

        if (directory)
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        else
            Assert.Equal("original", await File.ReadAllTextAsync(output));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(1, 10, 20)]
    [InlineData(2, 9, 20)]
    [InlineData(2, 10, 19)]
    public async Task EnforcesEntryCountEntryBytesAndTotalBytes(int maxEntries, long maxEntryBytes, long maxTotalBytes)
    {
        using var zip = CreateArchive(("first", "0123456789"), ("second", "0123456789"));
        var options = new ZipExtractionOptions { MaxEntries = maxEntries, MaxEntryBytes = maxEntryBytes, MaxTotalBytes = maxTotalBytes };
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output"), options));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task AcceptsExactByteLimitBoundaries()
    {
        using var zip = CreateArchive(("first", "0123456789"), ("second", "0123456789"));
        await ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output"),
            new ZipExtractionOptions { MaxEntries = 2, MaxEntryBytes = 10, MaxTotalBytes = 20 });
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "output")).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsForgedLengthsThatWouldHideInflatedContentFromByteLimits(bool totalLimit)
    {
        using var valid = CreateArchive(("file", new string('a', 200_000)));
        var bytes = valid.ToArray();
        // Forge the uncompressed lengths in both the local and central headers.
        for (var index = 0; index < bytes.Length - 28; index++)
        {
            var signature = BitConverter.ToUInt32(bytes, index);
            if (signature is 0x04034b50 or 0x02014b50)
                BitConverter.GetBytes(1).CopyTo(bytes, index + (signature == 0x04034b50 ? 22 : 24));
        }
        using var forged = new MemoryStream(bytes);
        var options = new ZipExtractionOptions
        {
            MaxEntryBytes = totalLimit ? 200_000 : 100_000,
            MaxTotalBytes = totalLimit ? 100_000 : 200_000
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(forged, Path.Combine(_root, "output"), options));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public void Crc32MatchesKnownVectorAndIncrementalUpdates()
    {
        var bytes = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xcbf43926U, ~ZipArchiveHelper.UpdateCrc32(uint.MaxValue, bytes));
        var first = ZipArchiveHelper.UpdateCrc32(uint.MaxValue, bytes.AsSpan(0, 4));
        Assert.Equal(0xcbf43926U, ~ZipArchiveHelper.UpdateCrc32(first, bytes.AsSpan(4)));
        Assert.Equal(0U, ~ZipArchiveHelper.UpdateCrc32(uint.MaxValue, []));
    }

    [Theory]
    [InlineData("file", "abc")]
    [InlineData("directory/", "")]
    public async Task RejectsForgedCrcForFilesAndEmptyDirectories(string name, string content)
    {
        using var zip = CreateArchive((name, content));
        var bytes = zip.ToArray();
        ModifyZipHeaders(bytes, (signature, offset) =>
        {
            var crcOffset = offset + (signature == 0x04034b50 ? 14 : 16);
            bytes[crcOffset] ^= 1;
        });
        using var forged = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(forged, Path.Combine(_root, "output")));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task RejectsDeclaredLengthLargerThanActualContent()
    {
        using var zip = CreateArchive(("file", "abc"));
        var bytes = zip.ToArray();
        ModifyZipHeaders(bytes, (signature, offset) =>
            BitConverter.GetBytes(4).CopyTo(bytes, offset + (signature == 0x04034b50 ? 22 : 24)));
        using var forged = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(forged, Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task RejectsTruncatedCompressedPayloadWithoutPublishing()
    {
        using var zip = CreateArchive(("file", new string('a', 200_000)));
        var bytes = zip.ToArray();
        ModifyZipHeaders(bytes, (signature, offset) =>
            BitConverter.GetBytes(3).CopyTo(bytes, offset + (signature == 0x04034b50 ? 18 : 20)));
        using var truncated = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(truncated, Path.Combine(_root, "output")));
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(unchecked((int)0xa1ff0000))] // Unix symbolic link.
    [InlineData(0x10000000)] // Unix FIFO.
    [InlineData(0x400)] // Windows reparse point.
    [InlineData(0x40000000)] // Directory attributes on a file name.
    public async Task RejectsLinksSpecialFilesAndConflictingAttributes(int attributes)
    {
        using var zip = CreateArchive(("file", "target"));
        using (var editable = new ZipArchive(zip, ZipArchiveMode.Update, leaveOpen: true))
            editable.Entries[0].ExternalAttributes = attributes;
        zip.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task RejectsDirectoryEntriesWithContent()
    {
        using var zip = CreateArchive(("directory/", "hidden payload"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(zip, Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task RefusesOutputFileWithinSourceTree()
    {
        var input = Directory.CreateDirectory(Path.Combine(_root, "input"));
        await using var destination = File.Create(Path.Combine(input.FullName, "output.zip"));
        await Assert.ThrowsAsync<ArgumentException>(() => ZipArchiveHelper.CreateZipAsync(input.FullName, destination));
        Assert.Equal(0, destination.Length);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task RejectsLiteralBackslashesInUnixSourceNamesRatherThanChangingTheTree()
    {
        if (OperatingSystem.IsWindows())
            return;
        var input = Directory.CreateDirectory(Path.Combine(_root, "input"));
        await File.WriteAllTextAsync(Path.Combine(input.FullName, "literal\\name"), "content");
        using var zip = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.CreateZipAsync(input.FullName, zip));
        Assert.True(File.Exists(Path.Combine(input.FullName, "literal\\name")));
    }

    [Fact]
    public async Task FailedPublicationPreservesACompetingDestinationAndRemovesOnlyItsStagingDirectory()
    {
        using var zip = CreateArchive(("file", "content"));
        var output = Path.Combine(_root, "output");
        using var input = new CallbackStream(zip.ToArray(), () =>
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "owner"), "other caller");
        });
        await Assert.ThrowsAsync<IOException>(() => ZipArchiveHelper.ExtractZipAsync(input, output));
        Assert.Equal("other caller", await File.ReadAllTextAsync(Path.Combine(output, "owner")));
        Assert.Single(Directory.GetFiles(output));
        AssertNoStaging();
    }

    [Fact]
    public async Task RejectsLinksInSourceTreeAndDestinationAncestors()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside"));
        await File.WriteAllTextAsync(Path.Combine(outside.FullName, "secret"), "secret");
        var input = Directory.CreateDirectory(Path.Combine(_root, "input"));
        var link = Path.Combine(input.FullName, "link");
        if (!TryCreateLink(link, outside.FullName))
            return; // Windows requires Developer Mode or the symbolic-link privilege.
        using var zip = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => ZipArchiveHelper.CreateZipAsync(input.FullName, zip));
        using var archive = CreateArchive(("file", "content"));
        await Assert.ThrowsAsync<IOException>(() => ZipArchiveHelper.ExtractZipAsync(archive, Path.Combine(link, "output")));
        Assert.False(Directory.Exists(Path.Combine(outside.FullName, "output")));
    }

    [Fact]
    public async Task RejectsNonSeekableInputBeforeReading()
    {
        using var input = new NonSeekableStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ZipArchiveHelper.ExtractZipAsync(input, Path.Combine(_root, "output")));
        Assert.False(input.WasRead);
        AssertNoStaging();
    }

    [Fact]
    public async Task CancellationDuringExtractionCleansOwnedFilesAndKeepsSourceOpen()
    {
        using var zip = CreateArchive(("file", new string('a', 200_000)));
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingStream(zip.ToArray(), cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveHelper.ExtractZipAsync(input,
            Path.Combine(_root, "output"), cancellationToken: cancellation.Token));
        Assert.True(input.CanRead);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        AssertNoStaging();
    }

    [Fact]
    public async Task CancellationBeforeCreationDoesNotWriteToDestination()
    {
        var input = Directory.CreateDirectory(Path.Combine(_root, "input"));
        using var zip = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveHelper.CreateZipAsync(input.FullName,
            zip, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, zip.Length);
        Assert.True(zip.CanWrite);
    }

    [Fact]
    public async Task RejectsCorruptArchiveAndNegativeLimitsWithoutPublishing()
    {
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("not a ZIP"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ZipArchiveHelper.ExtractZipAsync(invalid, Path.Combine(_root, "output")));
        using var zip = CreateArchive(("file", ""));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ZipArchiveHelper.ExtractZipAsync(zip,
            Path.Combine(_root, "output"), new ZipExtractionOptions { MaxEntryBytes = -1 }));
        AssertNoStaging();
    }

    private static MemoryStream CreateArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void ModifyZipHeaders(byte[] bytes, Action<uint, int> modify)
    {
        for (var index = 0; index < bytes.Length - 28; index++)
        {
            var signature = BitConverter.ToUInt32(bytes, index);
            if (signature is 0x04034b50 or 0x02014b50)
                modify(signature, index);
        }
    }

    private static bool TryCreateLink(string path, string target)
    {
        try { Directory.CreateSymbolicLink(path, target); return true; }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { return false; }
        catch (IOException) when (OperatingSystem.IsWindows()) { return false; }
    }

    private void AssertNoStaging() => Assert.Empty(Directory.EnumerateDirectories(_root, ".zip-*"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class NonSeekableStream : MemoryStream
    {
        public bool WasRead { get; private set; }
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            return base.Read(buffer, offset, count);
        }
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class CallbackStream(byte[] bytes, Action onRead) : MemoryStream(bytes)
    {
        private bool _invoked;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_invoked)
            {
                _invoked = true;
                onRead();
            }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
