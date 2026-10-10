using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Media.Images;
using NSubstitute;
using SkiaSharp;

namespace MS.Microservice.Lab.Tests.Tools;

public sealed class ImageBatchJobsTests
{
    [Theory]
    [InlineData(null, "wwwroot", false)]
    [InlineData(null, "wwwroot/jobs", false)]
    [InlineData("", "wwwroot/jobs", false)]
    [InlineData("", "private/jobs", true)]
    [InlineData("public-assets", "public-assets", false)]
    [InlineData("public-assets", "public-assets/jobs", false)]
    [InlineData(null, "wwwroot-backup/jobs", true)]
    [InlineData("public-assets", "public-assets-backup/jobs", true)]
    [InlineData("public-assets", "private/jobs", true)]
    public async Task StorageRoot_CannotExposePrivateInputsOrResultsThroughWebRoot(string? webRoot, string storage, bool allowed)
    {
        await using var scope = new ImageBatchTestScope();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(scope.Root);
        environment.WebRootPath.Returns(string.IsNullOrEmpty(webRoot) ? webRoot! : Path.Combine(scope.Root, webRoot));
        var options = Options.Create(new ImageBatchOptions { Enabled = true, StorageDirectory = storage });
        if (allowed)
        {
            using var jobs = new ImageBatchJobs(environment, options, new ControlledImageOperation(), NullLogger<ImageBatchJobs>.Instance);
            Assert.True(jobs.Enabled);
        }
        else Assert.Throws<ArgumentException>(() => new ImageBatchJobs(environment, options, new ControlledImageOperation(), NullLogger<ImageBatchJobs>.Instance));
    }

    [Fact]
    public async Task TwoActualPngs_CompleteInOrderAndExposeReadableResizedResults()
    {
        await using var scope = new ImageBatchTestScope();
        var first = ImageBatchTestScope.Png(SKColors.Red);
        var second = ImageBatchTestScope.Png(SKColors.Blue);
        using var input1 = new MemoryStream(first);
        using var input2 = new MemoryStream(second);
        var job = await scope.Jobs.SubmitAsync("alice", [new(input1, ".png"), new(input2, ".png")],
            new() { Resize = new(4, 3) });
        await scope.SubmitAsync([255]);
        await scope.StartAsync();
        var call1 = await scope.Operation.NextAsync();
        Assert.Equal(ImageBatchState.Running, scope.Jobs.Get("alice", job.Id)!.State);
        call1.UseActualImageProcessor();
        var call2 = await scope.Operation.NextAsync();
        Assert.Equal(1, scope.Jobs.Get("alice", job.Id)!.CompletedFiles);
        call2.UseActualImageProcessor();
        _ = await scope.Operation.NextAsync(); // The next job starts only after publication and terminal state.
        var completed = scope.Jobs.Get("alice", job.Id)!;
        Assert.Equal(ImageBatchState.Succeeded, completed.State);
        Assert.Equal(2, completed.CompletedFiles);
        Assert.Equal(2, completed.TotalFiles);
        Assert.NotNull(completed.FinishedAtUtc);
        Assert.Null(completed.ErrorCode);
        Assert.True(input1.CanRead && input2.CanRead);
        Assert.Equal(1, scope.Operation.MaximumActive);
        for (var index = 0; index < 2; index++)
        {
            var receipt = await scope.Jobs.OpenResultAsync("alice", job.Id, index);
            Assert.NotNull(receipt);
            Assert.Equal("image/png", receipt.ContentType);
            await using var download = receipt.Content;
            using var bytes = new MemoryStream();
            await download.CopyToAsync(bytes);
            using var image = SKBitmap.Decode(bytes.ToArray());
            Assert.Equal(4, image.Width);
            Assert.Equal(3, image.Height);
            Assert.Equal(index == 0 ? SKColors.Red : SKColors.Blue, image.GetPixel(1, 1));
            Assert.Equal(bytes.Length, completed.Outputs[index].Bytes);
            Assert.Equal("image/png", completed.Outputs[index].ContentType);
        }
        Assert.Null(await scope.Jobs.OpenResultAsync("alice", job.Id, -1));
        Assert.Null(await scope.Jobs.OpenResultAsync("alice", job.Id, 2));
    }

    [Fact]
    public async Task SingleWorker_FinishesAllFilesBeforeNextQueuedJobAndReportsProgress()
    {
        await using var scope = new ImageBatchTestScope();
        var first = await scope.SubmitAsync([11], [12]);
        var next = await scope.SubmitAsync([21]);
        await scope.StartAsync();
        var call1 = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 11 }, await call1.ReadInputAsync());
        Assert.Equal(ImageBatchState.Queued, scope.Jobs.Get("alice", next.Id)!.State);
        call1.Succeed([1]);
        var call2 = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 12 }, await call2.ReadInputAsync());
        Assert.Equal(1, scope.Jobs.Get("alice", first.Id)!.CompletedFiles);
        Assert.Equal(ImageBatchState.Running, scope.Jobs.Get("alice", first.Id)!.State);
        Assert.Empty(scope.Jobs.Get("alice", first.Id)!.Outputs);
        call2.Succeed([2]);
        var call3 = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 21 }, await call3.ReadInputAsync());
        Assert.Equal(ImageBatchState.Succeeded, scope.Jobs.Get("alice", first.Id)!.State);
        Assert.Equal(1, scope.Operation.MaximumActive);
    }

    [Fact]
    public async Task QueuedAndRunningCancellation_NeverPublishDownloadablePartialResults()
    {
        await using var scope = new ImageBatchTestScope();
        var running = await scope.SubmitAsync([1], [2]);
        var queued = await scope.SubmitAsync([3]);
        await scope.StartAsync();
        var active = await scope.Operation.NextAsync();
        Assert.Equal(ImageBatchState.Cancelled, scope.Jobs.Cancel("alice", queued.Id)!.State);
        Assert.True(scope.Jobs.Cancel("alice", running.Id)!.CancellationRequested);
        await active.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scope.SubmitAsync([255]);
        _ = await scope.Operation.NextAsync();
        foreach (var id in new[] { running.Id, queued.Id })
        {
            var status = scope.Jobs.Get("alice", id)!;
            Assert.Equal(ImageBatchState.Cancelled, status.State);
            Assert.Equal(0, status.CompletedFiles);
            Assert.NotNull(status.FinishedAtUtc);
            Assert.Empty(status.Outputs);
            Assert.Null(await scope.Jobs.OpenResultAsync("alice", id, 0));
        }
    }

    [Fact]
    public async Task FailedSecondFile_HidesFirstOutputAndRetainsRetryableInputs()
    {
        await using var scope = new ImageBatchTestScope();
        var job = await scope.SubmitAsync([1], [2]);
        await scope.SubmitAsync([255]);
        await scope.StartAsync();
        (await scope.Operation.NextAsync()).Succeed([10]);
        var second = await scope.Operation.NextAsync();
        Assert.Equal(1, scope.Jobs.Get("alice", job.Id)!.CompletedFiles);
        Assert.Null(await scope.Jobs.OpenResultAsync("alice", job.Id, 0));
        second.Fail(new IOException("private diagnostic"));
        _ = await scope.Operation.NextAsync();
        var failed = scope.Jobs.Get("alice", job.Id)!;
        Assert.Equal(ImageBatchState.Failed, failed.State);
        Assert.Equal("processing_failed", failed.ErrorCode);
        Assert.Equal(1, failed.CompletedFiles);
        Assert.Empty(failed.Outputs);
        Assert.Null(await scope.Jobs.OpenResultAsync("alice", job.Id, 0));
    }

    [Fact]
    public async Task OwnerIsolation_AndRetryCopiesInputsIntoAnIndependentNewJob()
    {
        await using var scope = new ImageBatchTestScope();
        var source = await scope.SubmitAsync([1, 2, 3]);
        Assert.Null(scope.Jobs.Get("bob", source.Id));
        Assert.Null(scope.Jobs.Cancel("bob", source.Id));
        Assert.Null(await scope.Jobs.RetryAsync("bob", source.Id));
        Assert.Null(await scope.Jobs.OpenResultAsync("bob", source.Id, 0));
        Assert.False(scope.Jobs.Delete("bob", source.Id));
        Assert.Throws<InvalidOperationException>(() => scope.Jobs.Delete("alice", source.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Jobs.RetryAsync("alice", source.Id));
        scope.Jobs.Cancel("alice", source.Id);
        var retry = await scope.Jobs.RetryAsync("alice", source.Id);
        Assert.NotNull(retry);
        Assert.NotEqual(source.Id, retry.Id);
        Assert.Equal(ImageBatchState.Queued, retry.State);
        Assert.Equal(0, retry.CompletedFiles);
        Assert.True(scope.Jobs.Delete("alice", source.Id));
        await scope.SubmitAsync([255]);
        await scope.StartAsync();
        var copied = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, await copied.ReadInputAsync());
        Assert.Throws<InvalidOperationException>(() => scope.Jobs.Delete("alice", retry.Id));
        copied.Succeed([9]);
        _ = await scope.Operation.NextAsync();
        Assert.Equal(ImageBatchState.Succeeded, scope.Jobs.Get("alice", retry.Id)!.State);
        Assert.Null(await scope.Jobs.OpenResultAsync("bob", retry.Id, 0));
        Assert.False(scope.Jobs.Delete("bob", retry.Id));
        Assert.True(scope.Jobs.Delete("alice", retry.Id));
        Assert.Null(scope.Jobs.Get("alice", retry.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryAndUndrainedQueueCapacity_RejectWithoutLeavingNewJobs(bool queueOnly)
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumJobs = 1 });
        var job = await scope.SubmitAsync([1]);
        if (queueOnly)
        {
            scope.Jobs.Cancel("alice", job.Id);
            Assert.True(scope.Jobs.Delete("alice", job.Id));
        }
        using var input = new MemoryStream([2]);
        await Assert.ThrowsAsync<ImageBatchCapacityException>(() => scope.Jobs.SubmitAsync("alice", [new(input, ".png")], new()));
        Assert.True(input.CanRead);
        Assert.Equal(queueOnly ? 0 : 1, Directory.GetDirectories(scope.StorageRoot).Length);
    }

    [Theory]
    [InlineData(3, 3, 6, true)]
    [InlineData(4, 1, 6, false)]
    [InlineData(3, 3, 5, false)]
    public async Task ActualInputByteBudgets_RespectExactPerFileAndTotalBounds(int first, int second, int total, bool succeeds)
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumFileBytes = 3, MaximumBatchBytes = total });
        using var one = new MemoryStream(new byte[first]);
        using var two = new MemoryStream(new byte[second]);
        var task = scope.Jobs.SubmitAsync("alice", [new(one, ".png"), new(two, ".png")], new());
        if (succeeds)
        {
            var result = await task;
            Assert.Equal(ImageBatchState.Queued, result.State);
            Assert.Equal(2, result.TotalFiles);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => task);
            Assert.Empty(Directory.Exists(scope.StorageRoot) ? Directory.GetDirectories(scope.StorageRoot) : []);
            Assert.Equal(ImageBatchState.Queued, (await scope.SubmitAsync([1])).State);
        }
        Assert.True(one.CanRead && two.CanRead);
        Assert.InRange(one.Position + two.Position, 0, Math.Min(first, 4) + Math.Min(second, 4));
    }

    [Fact]
    public async Task InputSnapshots_StartAtCurrentPositionAndKeepNonSeekableCallerOpen()
    {
        await using var scope = new ImageBatchTestScope();
        using var positioned = new MemoryStream([99, 1, 2]);
        positioned.Position = 1;
        using var nonseekable = new NonSeekableStream([3, 4]);
        var job = await scope.Jobs.SubmitAsync("alice", [new(positioned, ".png"), new(nonseekable, ".png")], new());
        await scope.StartAsync();
        var first = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 1, 2 }, await first.ReadInputAsync());
        first.Succeed([1]);
        var second = await scope.Operation.NextAsync();
        Assert.Equal(new byte[] { 3, 4 }, await second.ReadInputAsync());
        Assert.True(positioned.CanRead && nonseekable.CanRead);
        Assert.Equal(1, scope.Jobs.Get("alice", job.Id)!.CompletedFiles);
    }

    [Theory]
    [InlineData("async")]
    [InlineData("byte")]
    [InlineData("length")]
    public async Task OutputBudget_AppliesToEveryStreamContentMutation(string mode)
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumOutputBytes = 2 });
        var job = await scope.SubmitAsync([1]);
        await scope.SubmitAsync([255]);
        await scope.StartAsync();
        (await scope.Operation.NextAsync()).Use(async call =>
        {
            if (mode == "async") await call.Output.WriteAsync(new byte[] { 1, 2, 3 }, call.Token);
            else if (mode == "byte") for (var index = 0; index < 3; index++) call.Output.WriteByte(1);
            else call.Output.SetLength(3);
            return new(1, 1, ImageOutputFormat.Png);
        });
        _ = await scope.Operation.NextAsync();
        Assert.Equal(ImageBatchState.Failed, scope.Jobs.Get("alice", job.Id)!.State);
        Assert.Null(await scope.Jobs.OpenResultAsync("alice", job.Id, 0));
    }

    [Fact]
    public async Task Stop_CancelsActiveAndQueuedWorkAndRejectsNewSubmission()
    {
        await using var scope = new ImageBatchTestScope();
        var active = await scope.SubmitAsync([1]);
        var queued = await scope.SubmitAsync([2]);
        await scope.StartAsync();
        var call = await scope.Operation.NextAsync();
        await scope.Jobs.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await call.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ImageBatchState.Cancelled, scope.Jobs.Get("alice", active.Id)!.State);
        Assert.Equal(ImageBatchState.Cancelled, scope.Jobs.Get("alice", queued.Id)!.State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.SubmitAsync([3]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeInputs_DisposesEveryOwnedStreamAndPreservesAllFailures(bool hasPrimary)
    {
        var primary = new InvalidDataException("operation");
        var one = new DisposalFailureStream(new IOException("first cleanup"));
        var two = new DisposalFailureStream(new IOException("second cleanup"));
        using var healthy = new MemoryStream();
        var error = await Assert.ThrowsAsync<AggregateException>(() => ImageBatchJobs.DisposeInputsAsync([one, healthy, two], hasPrimary ? primary : null));
        Assert.Equal(hasPrimary ? 3 : 2, error.InnerExceptions.Count);
        if (hasPrimary) Assert.Same(primary, error.InnerExceptions[0]);
        Assert.Contains(one.Error, error.InnerExceptions);
        Assert.Contains(two.Error, error.InnerExceptions);
        Assert.False(healthy.CanRead);
        Assert.True(one.WasDisposed && two.WasDisposed);
    }

    private sealed class NonSeekableStream(byte[] value) : MemoryStream(value)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}

internal sealed class ImageBatchTestScope : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "MS.Microservice.ImageBatchTests", Guid.NewGuid().ToString("N"));
    internal string StorageRoot { get; }
    internal ImageBatchJobs Jobs { get; }
    internal ControlledImageOperation Operation { get; } = new();
    internal ImageBatchTestScope(ImageBatchOptions? options = null)
    {
        Directory.CreateDirectory(Root);
        options ??= new() { Enabled = true, StorageDirectory = "batches" };
        StorageRoot = Path.Combine(Root, options.StorageDirectory);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(Root);
        Jobs = new(environment, Options.Create(options), Operation, NullLogger<ImageBatchJobs>.Instance);
    }
    internal Task StartAsync() => Jobs.StartAsync(CancellationToken.None);
    internal async Task<ImageBatchSnapshot> SubmitAsync(params byte[][] contents)
    {
        var streams = contents.Select(value => new MemoryStream(value)).ToArray();
        try { return await Jobs.SubmitAsync("alice", streams.Select(s => new ImageBatchInput(s, ".png")).ToArray(), new()); }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }
    internal static byte[] Png(SKColor color)
    {
        using var image = new SKBitmap(8, 6);
        image.Erase(color);
        using var snapshot = SKImage.FromBitmap(image);
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        await Jobs.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Jobs.Dispose();
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.ImageBatchTests"));
        if (!string.Equals(Directory.GetParent(Path.GetFullPath(Root))!.FullName, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

internal sealed class ControlledImageOperation : IImageBatchOperation
{
    private readonly Channel<ImageOperationCall> _calls = Channel.CreateUnbounded<ImageOperationCall>();
    private int _active;
    private int _maximumActive;
    internal int MaximumActive => Volatile.Read(ref _maximumActive);
    internal Task<ImageOperationCall> NextAsync() => _calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    public async Task<ImageProcessingResult> ProcessAsync(Stream input, Stream output, ImageProcessingOptions options, CancellationToken token)
    {
        var active = Interlocked.Increment(ref _active);
        int previous;
        do { previous = Volatile.Read(ref _maximumActive); }
        while (active > previous && Interlocked.CompareExchange(ref _maximumActive, active, previous) != previous);
        var call = new ImageOperationCall(input, output, options, token);
        _calls.Writer.TryWrite(call);
        try { return await (await call.Action.Task.WaitAsync(token))(call); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { call.Cancelled.TrySetResult(); throw; }
        finally { Interlocked.Decrement(ref _active); }
    }
}

internal sealed class ImageOperationCall(Stream input, Stream output, ImageProcessingOptions options, CancellationToken token)
{
    internal Stream Input => input;
    internal Stream Output => output;
    internal CancellationToken Token => token;
    internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Func<ImageOperationCall, Task<ImageProcessingResult>>> Action { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Use(Func<ImageOperationCall, Task<ImageProcessingResult>> action) => Action.TrySetResult(action);
    internal void Succeed(byte[] bytes) => Use(async call => { await call.Output.WriteAsync(bytes, call.Token); return new(1, 1, ImageOutputFormat.Png); });
    internal void Fail(Exception error) => Action.TrySetException(error);
    internal void UseActualImageProcessor() => Use(_ => new ImageBatchOperation().ProcessAsync(input, output, options, token));
    internal async Task<byte[]> ReadInputAsync() { using var copy = new MemoryStream(); await input.CopyToAsync(copy); return copy.ToArray(); }
}

internal sealed class DisposalFailureStream(Exception error, byte[]? value = null) : MemoryStream(value ?? [])
{
    internal Exception Error => error;
    internal bool WasDisposed { get; private set; }
    public override ValueTask DisposeAsync() { WasDisposed = true; base.Dispose(true); return ValueTask.FromException(error); }
}
