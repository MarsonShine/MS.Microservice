using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.IO;
using MS.Microservice.Lab.Infrastructure.Uploads;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Lab.Application.Tools;

public sealed record ImageBatchOptions
{
    public bool Enabled { get; init; }
    public string StorageDirectory { get; init; } = ".tools/image-batches";
    public int MaximumJobs { get; init; } = 16;
    public int MaximumFiles { get; init; } = 8;
    public int MaximumFileBytes { get; init; } = 5 * 1024 * 1024;
    public int MaximumBatchBytes { get; init; } = 20 * 1024 * 1024;
    public int MaximumOutputBytes { get; init; } = 32 * 1024 * 1024;
}

public enum ImageBatchState { Preparing, Queued, Running, Succeeded, Failed, Cancelled }
public sealed record ImageBatchOutput(int Index, int Width, int Height, string ContentType, long Bytes);
public sealed record ImageBatchSnapshot(Guid Id, ImageBatchState State, int CompletedFiles, int TotalFiles,
    bool CancellationRequested, DateTimeOffset CreatedAtUtc, DateTimeOffset? FinishedAtUtc, string? ErrorCode, IReadOnlyList<ImageBatchOutput> Outputs);
public sealed record ImageBatchInput(Stream Content, string Extension);
public sealed record ImageBatchDownload(Stream Content, string ContentType);
public sealed class ImageBatchCapacityException() : InvalidOperationException("The local batch queue or retained-job limit is full.");

public interface IImageBatchOperation
{
    Task<ImageProcessingResult> ProcessAsync(Stream input, Stream output, ImageProcessingOptions options, CancellationToken cancellationToken);
}

public sealed class ImageBatchOperation : IImageBatchOperation
{
    public Task<ImageProcessingResult> ProcessAsync(Stream input, Stream output, ImageProcessingOptions options, CancellationToken cancellationToken)
        => new ImageProcessor().ProcessAsync(input, output, options, cancellationToken);
}

/// <summary>A single-process Lab queue with owner-scoped progress, cancellation and explicit retry. Metadata is lost on restart.</summary>
public sealed class ImageBatchJobs : BackgroundService
{
    private readonly ImageBatchOptions _options;
    private readonly string _directory;
    private readonly IImageBatchOperation _operation;
    private readonly ILogger<ImageBatchJobs> _logger;
    private readonly Channel<Job> _queue;
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    public bool Enabled => _options.Enabled;
    public int MaximumFiles => _options.MaximumFiles;
    public int MaximumFileBytes => _options.MaximumFileBytes;

    public ImageBatchJobs(IWebHostEnvironment environment, IOptions<ImageBatchOptions> options,
        IImageBatchOperation operation, ILogger<ImageBatchJobs> logger)
    {
        _options = options.Value;
        if (!SampleUploadOptions.IsSafeStorageDirectory(_options.StorageDirectory)
            || _options.MaximumJobs is < 1 or > 64 || _options.MaximumFiles is < 1 or > 16
            || _options.MaximumFileBytes < 1 || _options.MaximumFileBytes > SampleUploadOptions.MaximumConfiguredFileBytes
            || _options.MaximumBatchBytes < 1 || _options.MaximumBatchBytes > SampleUploadOptions.MaximumRequestBodyBytes || _options.MaximumOutputBytes < 1)
            throw new ArgumentException("Configure safe local batch storage and finite positive queue/file budgets.", nameof(options));
        _directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.StorageDirectory));
        var webRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            string.IsNullOrWhiteSpace(environment.WebRootPath) ? "wwwroot" : environment.WebRootPath));
        var relative = Path.GetRelativePath(webRoot, _directory);
        if (relative == "." || !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Batch files must stay outside the public web root.", nameof(options));
        _operation = operation;
        _logger = logger;
        _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(_options.MaximumJobs)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public async Task<ImageBatchSnapshot> SubmitAsync(string owner, IReadOnlyList<ImageBatchInput> inputs,
        ImageProcessingOptions processing, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(processing);
        if (!Enabled) throw new InvalidOperationException("The Lab image batch feature is disabled.");
        if (owner.Length > 256 || inputs.Count < 1 || inputs.Count > _options.MaximumFiles)
            throw new ArgumentException("Use a bounded owner and a nonempty batch within the file-count limit.");
        var items = inputs.ToArray();
        var job = new Job(owner, processing with { Limits = processing.Limits with { } }, inputs.Count, _directory);
        lock (_sync)
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            if (_jobs.Count >= _options.MaximumJobs) throw new ImageBatchCapacityException();
            _jobs.Add(job.Id, job);
        }
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            long total = 0;
            foreach (var item in items)
            {
                ArgumentNullException.ThrowIfNull(item);
                using var bytes = await ReadAsync(item.Content, (int)Math.Min(_options.MaximumFileBytes, _options.MaximumBatchBytes - total), stop.Token);
                total += bytes.Length;
                job.InputKeys.Add(await job.Storage.SaveAsync(bytes, item.Extension, stop.Token));
            }
            lock (_sync)
            {
                stop.Token.ThrowIfCancellationRequested();
                job.State = ImageBatchState.Queued;
                if (!_queue.Writer.TryWrite(job)) throw new ImageBatchCapacityException();
                return Snapshot(job);
            }
        }
        catch (Exception primary)
        {
            lock (_sync) _jobs.Remove(job.Id);
            try { DeleteDirectory(job); }
            catch (Exception cleanup) { throw new AggregateException("Batch submission failed and its staging directory could not be removed.", primary, cleanup); }
            finally { job.Cancel.Dispose(); }
            throw;
        }
    }

    public ImageBatchSnapshot? Get(string owner, Guid id)
    { lock (_sync) return Find(owner, id) is { } job ? Snapshot(job) : null; }

    public ImageBatchSnapshot? Cancel(string owner, Guid id)
    {
        lock (_sync)
        {
            if (Find(owner, id) is not { } job) return null;
            if (!Terminal(job.State))
            {
                job.Cancel.Cancel();
                if (job.State == ImageBatchState.Queued) Finish(job, ImageBatchState.Cancelled);
            }
            return Snapshot(job);
        }
    }

    public async Task<ImageBatchSnapshot?> RetryAsync(string owner, Guid id, CancellationToken cancellationToken = default)
    {
        Job source;
        lock (_sync)
        {
            if (Find(owner, id) is not { } job) return null;
            if (job.State is not (ImageBatchState.Failed or ImageBatchState.Cancelled) || job.Deleting)
                throw new InvalidOperationException("Only a failed or cancelled retained batch can be retried.");
            source = job;
            source.Readers++;
        }
        var streams = new List<Stream>();
        Exception? failure = null;
        try
        {
            foreach (var key in source.InputKeys) streams.Add(await source.Storage.OpenReadAsync(key, cancellationToken));
            return await SubmitAsync(owner, streams.Select((stream, index) => new ImageBatchInput(stream, Path.GetExtension(source.InputKeys[index]))).ToArray(),
                source.Processing, cancellationToken);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { await DisposeInputsAsync(streams, failure); }
            finally { lock (_sync) source.Readers--; }
        }
    }

    /// <summary>Opens a completed result with MIME metadata from the same snapshot. The caller owns the returned stream.</summary>
    public async Task<ImageBatchDownload?> OpenResultAsync(string owner, Guid id, int index, CancellationToken cancellationToken = default)
    {
        Job job;
        string key;
        string contentType;
        lock (_sync)
        {
            if (Find(owner, id) is not { State: ImageBatchState.Succeeded } found || found.Deleting
                || index < 0 || index >= found.OutputKeys.Count) return null;
            job = found;
            key = found.OutputKeys[index];
            contentType = found.Outputs[index].ContentType;
            job.Readers++;
        }
        try { return new(await job.Storage.OpenReadAsync(key, cancellationToken), contentType); }
        finally { lock (_sync) job.Readers--; }
    }

    public bool Delete(string owner, Guid id)
    {
        Job job;
        lock (_sync)
        {
            if (Find(owner, id) is not { } found) return false;
            if (!Terminal(found.State) || found.Readers != 0 || found.Deleting)
                throw new InvalidOperationException("Wait until processing and retained-input copying finish before deleting a batch.");
            job = found;
            job.Deleting = true;
        }
        try
        {
            DeleteDirectory(job);
            lock (_sync) _jobs.Remove(id);
            job.Cancel.Dispose();
            return true;
        }
        finally { lock (_sync) job.Deleting = false; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled) return;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _shutdown.Token);
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stopping.Token))
            {
                lock (_sync)
                {
                    if (job.State != ImageBatchState.Queued) continue;
                    job.State = ImageBatchState.Running;
                }
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, job.Cancel.Token);
                try
                {
                    foreach (var inputKey in job.InputKeys)
                    {
                        await using var input = await job.Storage.OpenReadAsync(inputKey, cancel.Token);
                        using var output = new LimitedBuffer(_options.MaximumOutputBytes);
                        var result = await _operation.ProcessAsync(input, output, job.Processing, cancel.Token);
                        output.Position = 0;
                        var extension = result.Format switch { ImageOutputFormat.Jpeg => ".jpg", ImageOutputFormat.Webp => ".webp", _ => ".png" };
                        var key = await job.Storage.SaveAsync(output, extension, cancel.Token);
                        lock (_sync)
                        {
                            job.OutputKeys.Add(key);
                            job.Outputs.Add(new(job.Outputs.Count, result.Width, result.Height,
                                result.Format switch { ImageOutputFormat.Jpeg => "image/jpeg", ImageOutputFormat.Webp => "image/webp", _ => "image/png" }, output.Length));
                            job.Completed++;
                        }
                    }
                    lock (_sync)
                    {
                        cancel.Token.ThrowIfCancellationRequested();
                        Finish(job, ImageBatchState.Succeeded);
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                { lock (_sync) Finish(job, ImageBatchState.Cancelled); }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Image batch {BatchId} failed.", job.Id);
                    lock (_sync) { job.Error = "processing_failed"; Finish(job, ImageBatchState.Failed); }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            lock (_sync)
                foreach (var job in _jobs.Values.Where(job => !Terminal(job.State))) Finish(job, ImageBatchState.Cancelled);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    { _shutdown.Cancel(); _queue.Writer.TryComplete(); return base.StopAsync(cancellationToken); }

    internal static async Task DisposeInputsAsync(IEnumerable<Stream> streams, Exception? primary)
    {
        var errors = new List<Exception>();
        foreach (var stream in streams)
        {
            try { await stream.DisposeAsync(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 0)
            throw new AggregateException("Batch input cleanup failed.", primary is null ? errors : [primary, .. errors]);
    }

    private Job? Find(string owner, Guid id) => _jobs.TryGetValue(id, out var job) && job.Owner == owner ? job : null;
    private static bool Terminal(ImageBatchState state) => state is ImageBatchState.Succeeded or ImageBatchState.Failed or ImageBatchState.Cancelled;
    private static void Finish(Job job, ImageBatchState state) { job.State = state; job.Finished = DateTimeOffset.UtcNow; }
    private static ImageBatchSnapshot Snapshot(Job job) => new(job.Id, job.State, job.Completed, job.Total,
        job.Cancel.IsCancellationRequested, job.Created, job.Finished, job.Error,
        Array.AsReadOnly(job.State == ImageBatchState.Succeeded ? job.Outputs.ToArray() : []));

    private void DeleteDirectory(Job job)
    {
        if (!string.Equals(Directory.GetParent(Path.GetFullPath(job.Directory))?.FullName,
            Path.TrimEndingDirectorySeparator(_directory), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The batch directory is outside its configured storage root.");
        if (Directory.Exists(job.Directory)) Directory.Delete(job.Directory, recursive: true);
    }

    private static async Task<MemoryStream> ReadAsync(Stream input, int maximum, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var output = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, (long)maximum - output.Length + 1)), cancellationToken)) != 0)
            {
                if (read > maximum - output.Length) throw new InvalidDataException("The batch exceeds its actual encoded-byte budget.");
                output.Write(buffer, 0, read);
            }
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private sealed class Job(string owner, ImageProcessingOptions processing, int total, string root)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string Owner { get; } = owner;
        internal ImageProcessingOptions Processing { get; } = processing;
        internal int Total { get; } = total;
        internal DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
        internal ImageBatchState State = ImageBatchState.Preparing;
        internal int Completed, Readers;
        internal bool Deleting;
        internal DateTimeOffset? Finished;
        internal string? Error;
        internal CancellationTokenSource Cancel { get; } = new();
        internal List<string> InputKeys { get; } = [];
        internal List<string> OutputKeys { get; } = [];
        internal List<ImageBatchOutput> Outputs { get; } = [];
        internal string Directory => Path.Combine(root, Id.ToString("N"));
        internal LocalFileStorage Storage => new(Directory);
    }

    private sealed class LimitedBuffer(int maximum) : MemoryStream
    {
        private void Check(int count) { if (Position > (long)maximum - count) throw new InvalidDataException("An image result exceeds its output-byte budget."); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value)
        {
            if (value > maximum) throw new InvalidDataException("An image result exceeds its output-byte budget.");
            base.SetLength(value);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { Check(count); return base.WriteAsync(buffer, offset, count, token); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, token); }
    }
}
