using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using MS.Microservice.Core.IO;

namespace MS.Microservice.Storage.S3;

/// <summary>Optional S3 storage; the host owns the SDK client, credentials and authorization.</summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly S3FileStorageOptions _options;
    private readonly string _stagingParent;
    private readonly string _prefix;

    public S3FileStorage(IAmazonS3 client, S3FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TemporaryDirectory);
        ArgumentNullException.ThrowIfNull(options.Prefix);
        if (options.BucketName != options.BucketName.Trim() || options.MaximumUploadBytes is < 0 or > 5L * 1024 * 1024 * 1024)
            throw new ArgumentException("Choose a bucket without surrounding whitespace and a single-PUT upload budget from zero to 5 GiB.", nameof(options));
        var prefix = options.Prefix;
        if (prefix.Length > 981 || (prefix.Length != 0 && prefix.Split('/').Any(segment =>
            segment.Length == 0 || segment is "." or ".." || segment.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))))
            throw new ArgumentException("Prefix must be canonical safe ASCII segments within the S3 key-length limit.", nameof(options));
        _client = client;
        _options = options;
        _prefix = prefix.Length == 0 ? "" : prefix + "/";
        _stagingParent = Path.GetFullPath(options.TemporaryDirectory);
    }

    public async Task WriteNewAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(key);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("Content must be readable.", nameof(content));
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_stagingParent)) throw new DirectoryNotFoundException("The trusted S3 staging parent must exist.");
        var staging = Path.Combine(_stagingParent, ".s3-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("The staging directory already exists.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(staging);
        else Directory.CreateDirectory(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Exception? primary = null;
        try
        {
            var local = new LocalFileStorage(staging);
            using var bounded = new UploadReadStream(content, _options.MaximumUploadBytes);
            var stagedKey = await local.SaveAsync(bounded, key[32..], cancellationToken).ConfigureAwait(false);
            await using var upload = await local.OpenReadAsync(stagedKey, cancellationToken).ConfigureAwait(false);
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName, Key = objectKey, InputStream = upload, IfNoneMatch = "*",
                AutoCloseStream = false, AutoResetStreamPosition = false, UseChunkEncoding = false,
                ContentType = "application/octet-stream"
            };
            request.Headers.ContentLength = upload.Length;
            try { await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false); }
            catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.PreconditionFailed)
            { throw new IOException("The stored object already exists.", error); }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception cleanup) when (primary is not null && cleanup is IOException or UnauthorizedAccessException)
            { throw new AggregateException("S3 upload and local staging cleanup both failed.", primary, cleanup); }
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        GetObjectResponse response;
        try
        {
            response = await _client.GetObjectAsync(new GetObjectRequest
                { BucketName = _options.BucketName, Key = objectKey }, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception error) when (IsMissingObject(error))
        { throw new FileNotFoundException("The stored object does not exist.", key, error); }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.ResponseStream is null) throw new InvalidDataException("S3 returned no object stream.");
            return new ResponseReadStream(response);
        }
        catch (Exception primary)
        {
            try { response.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("S3 response acquisition and cleanup both failed.", primary, cleanup); }
            throw;
        }
    }

    public async Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                { BucketName = _options.BucketName, Key = objectKey }, cancellationToken).ConfigureAwait(false);
            if (response.ContentLength < 0 || response.LastModified is not { } modified)
                throw new InvalidDataException("S3 returned incomplete object metadata.");
            return new(key, response.ContentLength, new DateTimeOffset(modified.ToUniversalTime()));
        }
        catch (AmazonS3Exception error) when (IsMissingObject(error)) { return null; }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
                { BucketName = _options.BucketName, Key = objectKey }, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception error) when (IsMissingObject(error)) { }
    }

    /// <summary>Creates a GET bearer URL. The host must authorize access before calling this method.</summary>
    public async Task<string> CreateReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(key);
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Presigned GET URLs last from one second to seven days.");
        cancellationToken.ThrowIfCancellationRequested();
        var url = await _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName, Key = objectKey, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(lifetime)
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return url;
    }

    private string ObjectKey(string key) { StoredFileKey.Validate(key); return _prefix + key; }
    private static bool IsMissingObject(AmazonS3Exception error) => error.StatusCode == HttpStatusCode.NotFound &&
        error.ErrorCode is null or "" or "NoSuchKey" or "NotFound" or "404";

    private sealed class UploadReadStream(Stream source, long limit) : Stream
    {
        private long _remaining = limit;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => buffer.Length == 0 ? 0 : Record(source.Read(buffer[..Allowed(buffer.Length)]));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return buffer.Length == 0 ? 0 : Record(await source.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken).ConfigureAwait(false));
        }
        private int Allowed(int length) => (int)Math.Min(length, _remaining + 1);
        private int Record(int count)
        {
            if (count > _remaining) throw new InvalidDataException("The upload exceeds its configured byte budget.");
            _remaining -= count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
    }

    private sealed class ResponseReadStream(GetObjectResponse response) : Stream
    {
        private GetObjectResponse? _response = response;
        private Stream Inner => _response?.ResponseStream ?? throw new ObjectDisposedException(nameof(ResponseReadStream));
        public override bool CanRead => _response?.ResponseStream.CanRead ?? false;
        public override bool CanSeek => _response?.ResponseStream.CanSeek ?? false;
        public override bool CanWrite => false;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => Inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            var owned = Interlocked.Exchange(ref _response, null);
            if (disposing) owned?.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
