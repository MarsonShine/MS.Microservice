using MS.Microservice.Core.IO;
using Xunit;

namespace MS.Microservice.Core.Tests.IO;

public sealed class FileStorageLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.FileStorageLifecycleTests", Guid.NewGuid().ToString("N"));
    private IFileStorage Storage => new LocalFileStorage(_root);

    [Fact]
    public async Task WriteReadMetadataAndIdempotentDelete_UseOneOpaqueKey()
    {
        var key = StoredFileKey.Create(".BIN");
        using var input = new MemoryStream([0, 1, 2, 3]);
        input.Position = 1;
        var storage = Storage;
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await storage.WriteNewAsync(key, input);
        Assert.True(input.CanRead);
        var metadata = Assert.IsType<StoredFileMetadata>(await storage.GetMetadataAsync(key));
        Assert.Equal(key, metadata.Key);
        Assert.Equal(3, metadata.Length);
        Assert.InRange(metadata.LastModifiedUtc, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, metadata.LastModifiedUtc.Offset);
        await using (var read = await storage.OpenReadAsync(key))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
        }
        await storage.DeleteAsync(key);
        await storage.DeleteAsync(key);
        Assert.Null(await storage.GetMetadataAsync(key));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await storage.OpenReadAsync(key));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task MissingStorageDirectory_ReadMetadataAndDeleteDoNotCreateIt(int missingDepth)
    {
        var key = StoredFileKey.Create(".bin");
        var directory = Enumerable.Range(0, missingDepth).Aggregate(_root, (parent, index) => Path.Combine(parent, "missing" + index));
        IFileStorage storage = new LocalFileStorage(directory);
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await storage.OpenReadAsync(key));
        Assert.Null(await storage.GetMetadataAsync(key));
        await storage.DeleteAsync(key);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../file.bin")]
    [InlineData("C:\\outside.bin")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.BIN")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bin ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.a:b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.a/b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.a\\b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.中")]
    [InlineData(".aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.tmp")]
    public async Task InvalidOrAliasedKeys_CannotAddressTheFilesystem(string key)
    {
        using var input = new MemoryStream([1, 2]);
        var storage = Storage;
        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.WriteNewAsync(key, input));
        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.GetMetadataAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.DeleteAsync(key));
        Assert.Equal(0, input.Position);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task ExistingKey_IsNeverReplacedOrReadFromTheNewInput()
    {
        var key = StoredFileKey.Create(".bin");
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);
        await Storage.WriteNewAsync(key, first);
        await Assert.ThrowsAsync<IOException>(async () => await Storage.WriteNewAsync(key, second));
        Assert.Equal(0, second.Position);
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(_root, key)));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ConcurrentWritesToSameKey_PublishExactlyOneCompleteFile()
    {
        var key = StoredFileKey.Create(".bin");
        var tasks = Enumerable.Range(1, 8).Select(async value =>
        {
            using var input = new MemoryStream(Enumerable.Repeat((byte)value, 4096).ToArray());
            try { await Storage.WriteNewAsync(key, input); return true; }
            catch (IOException) { return false; }
        });
        Assert.Single(await Task.WhenAll(tasks), success => success);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, key));
        Assert.Equal(4096, bytes.Length);
        Assert.All(bytes, value => Assert.Equal(bytes[0], value));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task OpenRead_RemainsUsableAfterDeletingItsKey()
    {
        var key = StoredFileKey.Create(".bin");
        using var input = new MemoryStream([7, 8]);
        await Storage.WriteNewAsync(key, input);
        await using var read = await Storage.OpenReadAsync(key);
        await Storage.DeleteAsync(key);
        Assert.Null(await Storage.GetMetadataAsync(key));
        Assert.Equal(7, read.ReadByte());
        Assert.Equal(8, read.ReadByte());
    }

    [Fact]
    public async Task PreCancelledOperations_DoNotCreateOrDeleteAnything()
    {
        var key = StoredFileKey.Create(".bin");
        using var input = new MemoryStream([9]);
        var storage = Storage;
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.WriteNewAsync(key, input, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.OpenReadAsync(key, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.GetMetadataAsync(key, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.DeleteAsync(key, token));
        Assert.False(Directory.Exists(_root));
        await storage.WriteNewAsync(key, input);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.DeleteAsync(key, token));
        Assert.NotNull(await storage.GetMetadataAsync(key));
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.FileStorageLifecycleTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
