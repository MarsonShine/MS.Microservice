using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using MS.Microservice.Core.IO;
using NSubstitute;
using Xunit;

namespace MS.Microservice.Storage.S3.Tests;

public sealed class S3FileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.S3FileStorageTests", Guid.NewGuid().ToString("N"));
    public S3FileStorageTests() => Directory.CreateDirectory(_root);
    private S3FileStorageOptions Options(long maximum = 64 * 1024 * 1024, string prefix = "tools/files")
        => new() { BucketName = "test-bucket", Prefix = prefix, TemporaryDirectory = _root, MaximumUploadBytes = maximum };

    [Fact]
    public async Task RealSdkSignedConditionalPutHeadGetDeleteRoundTripsCurrentPositionAndMetadata()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".BIN");
        using var input = new MemoryStream([99, 1, 2, 3]);
        input.Position = 1;
        await storage.WriteNewAsync(key, input);
        Assert.True(input.CanRead);
        var metadata = await storage.GetMetadataAsync(key);
        Assert.NotNull(metadata);
        Assert.Equal((key, 3), (metadata.Key, metadata.Length));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), metadata.LastModifiedUtc);
        var downloaded = await storage.OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);
        await downloaded.DisposeAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray());
        Assert.False(downloaded.CanRead);
        Assert.NotNull(await storage.GetMetadataAsync(key)); // Disposing the response never owns the client.
        await storage.DeleteAsync(key);
        await storage.DeleteAsync(key);
        Assert.Null(await storage.GetMetadataAsync(key));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(key));
        Assert.All(server.Requests, request => Assert.True(request.SignatureValid));
        var put = Assert.Single(server.Requests, request => request.Method == "PUT");
        Assert.Equal("*", put.IfNoneMatch);
        Assert.True(put.HasChecksum);
        Assert.Equal("/test-bucket/tools/files/" + key, put.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, put.Body);
        AssertStagingEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task NonseekableShortReadsEmptyAndExactBudgetUploadWithoutClosingInput(int length)
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options(length, ""));
        using var input = new ObservedStream(Enumerable.Range(0, length).Select(value => (byte)value).ToArray(), 2);
        var key = StoredFileKey.Create(".txt");
        await storage.WriteNewAsync(key, input);
        Assert.True(input.CanRead);
        Assert.Equal(length, input.BytesRead);
        Assert.Equal(length, (await storage.GetMetadataAsync(key))!.Length);
        Assert.Equal("/test-bucket/" + key, Assert.Single(server.Requests, request => request.Method == "PUT").Path);
        AssertStagingEmpty();
    }

    [Fact]
    public async Task OverBudgetConsumesAtMostOneExtraByteAndMakesNoRemoteRequest()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        using var input = new ObservedStream(new byte[20]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new S3FileStorage(client, Options(5)).WriteNewAsync(StoredFileKey.Create(".bin"), input));
        Assert.Equal(6, input.BytesRead);
        Assert.True(input.CanRead);
        Assert.Empty(server.Requests);
        AssertStagingEmpty();
    }

    [Fact]
    public async Task ConcurrentWritersUseServerConditionAndNeverOverwriteOrDeleteTheWinner()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".bin");
        async Task<Exception?> Write(byte value)
        {
            using var source = new MemoryStream([value]);
            try { await storage.WriteNewAsync(key, source); return null; }
            catch (Exception error) { return error; }
        }
        var results = await Task.WhenAll(Write(1), Write(2));
        Assert.Single(results, error => error is null);
        Assert.IsType<IOException>(Assert.Single(results, error => error is not null));
        Assert.Contains(server.Objects.Values.Single()[0], new byte[] { 1, 2 });
        Assert.Equal(2, server.Requests.Count(request => request.Method == "PUT"));
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
        Assert.All(server.Requests, request => Assert.Equal("*", request.IfNoneMatch));
        AssertStagingEmpty();
    }

    [Fact]
    public async Task CancellationAfterRemoteCommitDoesNotAttemptRemoteRollback()
    {
        await using var server = await S3WireServer.StartAsync();
        server.HoldPutResponse = true;
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        using var input = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        var key = StoredFileKey.Create(".bin");
        var write = storage.WriteNewAsync(key, input, cancellation.Token);
        await server.PutCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.True(input.CanRead);
        Assert.NotNull(await storage.GetMetadataAsync(key));
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
        AssertStagingEmpty();
    }

    [Fact]
    public async Task DisposingDownloadOwnsTheHttpResponseAndAbortsUnreadResponseBody()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".bin");
        using var input = new MemoryStream([42]);
        await storage.WriteNewAsync(key, input);
        server.HoldDownload = true;
        var stream = await storage.OpenReadAsync(key);
        var byteBuffer = new byte[1];
        Assert.Equal(1, await stream.ReadAsync(byteBuffer));
        Assert.Equal(42, byteBuffer[0]);
        await stream.DisposeAsync();
        await server.DownloadAborted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(stream.CanRead);
        Assert.NotNull(await storage.GetMetadataAsync(key));
    }

    [Fact]
    public async Task ActualSdk403AndRecognizableMissingBucketAreNotReportedAsMissingObject()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".bin");
        server.ForceErrorStatus = HttpStatusCode.Forbidden;
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.GetMetadataAsync(key))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.OpenReadAsync(key))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.DeleteAsync(key))).StatusCode);
        server.ForceErrorStatus = HttpStatusCode.NotFound;
        server.ForceErrorCode = "NoSuchBucket";
        Assert.Equal("NoSuchBucket", (await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.OpenReadAsync(key))).ErrorCode);
    }

    [Fact]
    public async Task MetadataBucket404AndMissingHeadersKeepTheirDistinctFaults()
    {
        var client = Substitute.For<IAmazonS3>();
        var key = StoredFileKey.Create(".bin");
        var bucketError = new AmazonS3Exception("wrong bucket") { StatusCode = HttpStatusCode.NotFound, ErrorCode = "NoSuchBucket" };
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GetObjectMetadataResponse>(bucketError));
        var storage = new S3FileStorage(client, Options());
        Assert.Same(bucketError, await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.GetMetadataAsync(key)));
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 3 });
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.GetMetadataAsync(key));
        client.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task CancellationWhileAcquiringResponsePreservesCancellationAndResponseCleanupFailure()
    {
        var client = Substitute.For<IAmazonS3>();
        using var cancellation = new CancellationTokenSource();
        var cleanup = new IOException("response cleanup failed");
        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return new GetObjectResponse { ResponseStream = new DisposeFailureStream(cleanup) };
        });
        var error = await Assert.ThrowsAsync<AggregateException>(() => new S3FileStorage(client, Options())
            .OpenReadAsync(StoredFileKey.Create(".bin"), cancellation.Token));
        Assert.Contains(error.InnerExceptions, primary => primary is OperationCanceledException);
        Assert.Contains(cleanup, error.InnerExceptions);
        client.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task PresignedGetUrlsHaveBoundedLifetimeCanonicalKeyAndNoNetworkSideEffect()
    {
        await using var server = await S3WireServer.StartAsync();
        using var client = server.CreateClient();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".bin");
        var url = new Uri(await storage.CreateReadUrlAsync(key, TimeSpan.FromDays(7)));
        Assert.Equal("/test-bucket/tools/files/" + key, url.AbsolutePath);
        var query = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        Assert.Equal("AWS4-HMAC-SHA256", query["X-Amz-Algorithm"]);
        Assert.InRange(int.Parse(query["X-Amz-Expires"]), 604_798, 604_800);
        Assert.Contains(S3WireServer.AccessKey, query["X-Amz-Credential"]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => storage.CreateReadUrlAsync(key, TimeSpan.FromTicks(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => storage.CreateReadUrlAsync(key, TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1)));
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("../key")]
    [InlineData("00000000000000000000000000000000.TXT")]
    [InlineData("00000000000000000000000000000000.bin/../other")]
    [InlineData("00000000000000000000000000000000.bin%2fother")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")]
    public async Task MaliciousOrAliasedKeysNeverReachSdk(string key)
    {
        var client = Substitute.For<IAmazonS3>();
        var storage = new S3FileStorage(client, Options());
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.WriteNewAsync(key, input));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetMetadataAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.CreateReadUrlAsync(key, TimeSpan.FromMinutes(1)));
        Assert.Empty(client.ReceivedCalls());
        AssertStagingEmpty();
    }

    [Theory]
    [InlineData("/team")]
    [InlineData("team/")]
    [InlineData("team//files")]
    [InlineData("../team")]
    [InlineData("team/./files")]
    [InlineData("team\\files")]
    [InlineData("team%2ffiles")]
    public void InvalidPrefixesAreConfigurationErrors(string prefix)
        => Assert.Throws<ArgumentException>(() => new S3FileStorage(Substitute.For<IAmazonS3>(), Options(prefix: prefix)));

    [Fact]
    public async Task PreCancellationAndReadFailureMakeNoSdkCallAndCleanOwnedStaging()
    {
        var client = Substitute.For<IAmazonS3>();
        var storage = new S3FileStorage(client, Options());
        var key = StoredFileKey.Create(".bin");
        using var input = new MemoryStream([1]);
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.WriteNewAsync(key, input, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.OpenReadAsync(key, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.GetMetadataAsync(key, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.DeleteAsync(key, cancelled));
        var failure = new IOException("source failed");
        using var broken = new BrokenStream(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => storage.WriteNewAsync(key, broken)));
        Assert.True(broken.CanRead);
        Assert.Empty(client.ReceivedCalls());
        AssertStagingEmpty();
    }

    [Fact]
    public async Task CleanupFailurePreservesPrimaryUploadErrorAndDoesNotDeleteRemoteObject()
    {
        if (!OperatingSystem.IsWindows()) return;
        var client = Substitute.For<IAmazonS3>();
        var primary = new IOException("remote transport failed");
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<PutObjectRequest>();
            Assert.True(request.InputStream.CanSeek);
            Assert.Equal(0, request.InputStream.Position);
            Assert.False(request.AutoCloseStream);
            Assert.False(request.AutoResetStreamPosition);
            File.SetAttributes(((FileStream)request.InputStream).Name, FileAttributes.ReadOnly);
            return Task.FromException<PutObjectResponse>(primary);
        });
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<AggregateException>(() => new S3FileStorage(client, Options()).WriteNewAsync(StoredFileKey.Create(".bin"), input));
        Assert.Contains(primary, error.InnerExceptions);
        Assert.Contains(error.InnerExceptions, failure => failure is UnauthorizedAccessException);
        Assert.True(input.CanRead);
        await client.DidNotReceive().DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>());
    }

    private void AssertStagingEmpty() => Assert.Empty(Directory.EnumerateFileSystemEntries(_root));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.S3FileStorageTests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture path.");
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class ObservedStream(byte[] bytes, int maximumRead = int.MaxValue) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumRead)], cancellationToken);
            BytesRead += count;
            return count;
        }
    }

    private sealed class BrokenStream(Exception error) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(error);
    }

    private sealed class DisposeFailureStream(Exception error) : MemoryStream
    {
        protected override void Dispose(bool disposing) { base.Dispose(disposing); throw error; }
    }
}
