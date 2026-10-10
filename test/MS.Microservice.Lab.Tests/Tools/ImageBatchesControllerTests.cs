using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Lab.Infrastructure.Uploads;
using SkiaSharp;

namespace MS.Microservice.Lab.Tests.Tools;

public sealed class ImageBatchesControllerTests
{
    [Fact]
    public void Endpoint_RequiresAuthorizationLabHostAndTwentyFiveMiBMultipartCap()
    {
        var type = typeof(ImageBatchesController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(type.GetCustomAttribute<LabOnlyAttribute>());
        var limit = Assert.Single(type.CustomAttributes, attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(25L * 1024 * 1024, Assert.IsType<long>(Assert.Single(limit.ConstructorArguments).Value));
        Assert.Equal(SampleUploadOptions.MaximumRequestBodyBytes, type.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        var feature = new ControllerFeature();
        feature.Controllers.Add(type.GetTypeInfo());
        new LabOnlyControllerFeatureProvider(false).PopulateFeature(Array.Empty<ApplicationPart>(), feature);
        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public async Task ValidationRejectsBadFileBeforeCreatingAnyJobOrOpeningSubmissionStreams()
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumJobs = 1 });
        var valid = Form(ImageBatchTestScope.Png(SKColors.Red));
        var invalid = Form("bad-file"u8.ToArray());
        var controller = Controller(scope);
        var response = Assert.IsType<ObjectResult>(await controller.Submit(new() { Files = [valid, invalid] }));
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
        Assert.Equal(1, valid.OpenCount); // Validation only: the batch never owns another stream.
        Assert.Equal(1, invalid.OpenCount);
        Assert.False(Directory.Exists(scope.StorageRoot));
        var accepted = Assert.IsType<AcceptedAtActionResult>(await controller.Submit(new() { Files = [Form(ImageBatchTestScope.Png(SKColors.Blue))] }));
        var job = Assert.IsType<ImageBatchSnapshot>(accepted.Value);
        Assert.Equal(nameof(ImageBatchesController.Get), accepted.ActionName);
        Assert.Equal(ImageBatchState.Queued, job.State);
        Assert.Equal(1, job.TotalFiles);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("size")]
    [InlineData("pair")]
    [InlineData("quality")]
    [InlineData("empty")]
    public async Task InvalidForm_IsRejectedBeforeValidationOrJobCreation(string kind)
    {
        await using var scope = new ImageBatchTestScope();
        var file = Form(ImageBatchTestScope.Png(SKColors.Red));
        var form = new ImageBatchForm { Files = [file] };
        if (kind == "format") form.Format = (MS.Microservice.Media.Images.ImageOutputFormat)99;
        if (kind == "size") { form.Width = 4096; form.Height = 4096; }
        if (kind == "pair") form.Width = 10;
        if (kind == "quality") form.Quality = 101;
        if (kind == "empty") form.Files.Clear();
        Assert.IsType<BadRequestObjectResult>(await Controller(scope).Submit(form));
        Assert.Equal(0, file.OpenCount);
        Assert.False(Directory.Exists(scope.StorageRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullRegistryOrUndrainedQueue_Returns429AndDisposesControllerOwnedInputs(bool queueOnly)
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumJobs = 1 });
        var existing = await scope.SubmitAsync([1]);
        if (queueOnly) { scope.Jobs.Cancel("alice", existing.Id); scope.Jobs.Delete("alice", existing.Id); }
        var file = Form(ImageBatchTestScope.Png(SKColors.Red));
        var response = Assert.IsType<ObjectResult>(await Controller(scope).Submit(new() { Files = [file] }));
        Assert.Equal(StatusCodes.Status429TooManyRequests, response.StatusCode);
        Assert.Equal(2, file.OpenCount);
        Assert.All(file.Opened, s => Assert.False(s.CanRead));
    }

    [Fact]
    public async Task ActualTotalBudget_Returns413AndDisposesEveryOwnedStream()
    {
        var content = ImageBatchTestScope.Png(SKColors.Red);
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches",
            MaximumFileBytes = content.Length, MaximumBatchBytes = content.Length * 2 - 1 });
        var first = Form(content);
        var second = Form(content);
        var response = Assert.IsType<ObjectResult>(await Controller(scope).Submit(new() { Files = [first, second] }));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, response.StatusCode);
        Assert.All(first.Opened.Concat(second.Opened), s => Assert.False(s.CanRead));
        Assert.Empty(Directory.GetDirectories(scope.StorageRoot));
    }

    [Fact]
    public async Task EveryRetainedJobEndpoint_IsOwnerScopedAndMissingIdentityIsUnauthorized()
    {
        await using var scope = new ImageBatchTestScope();
        var job = await scope.SubmitAsync([1]);
        var intruder = Controller(scope, "bob");
        Assert.IsType<NotFoundResult>(intruder.Get(job.Id));
        Assert.IsType<NotFoundResult>(intruder.Cancel(job.Id));
        Assert.IsType<NotFoundResult>(await intruder.Retry(job.Id));
        Assert.IsType<NotFoundResult>(await intruder.Download(job.Id, 0));
        Assert.IsType<NotFoundResult>(intruder.Delete(job.Id));
        Assert.Equal(ImageBatchState.Queued, scope.Jobs.Get("alice", job.Id)!.State);
        var missing = Controller(scope, null);
        Assert.IsType<UnauthorizedResult>(missing.Get(job.Id));
        Assert.IsType<UnauthorizedResult>(missing.Cancel(job.Id));
        Assert.IsType<UnauthorizedResult>(await missing.Retry(job.Id));
        Assert.IsType<UnauthorizedResult>(await missing.Download(job.Id, 0));
        Assert.IsType<UnauthorizedResult>(missing.Delete(job.Id));
        Assert.IsType<UnauthorizedResult>(await missing.Submit(new() { Files = [Form(ImageBatchTestScope.Png(SKColors.Red))] }));
        var owner = Controller(scope);
        Assert.IsType<OkObjectResult>(owner.Get(job.Id));
        Assert.IsType<ConflictObjectResult>(owner.Delete(job.Id));
        Assert.IsType<ConflictObjectResult>(await owner.Retry(job.Id));
        Assert.IsType<OkObjectResult>(owner.Cancel(job.Id));
        Assert.IsType<AcceptedAtActionResult>(await owner.Retry(job.Id));
        Assert.IsType<NoContentResult>(owner.Delete(job.Id));
    }

    [Fact]
    public async Task DisabledFeature_DoesNotOpenFilesOrExposeRetainedJobActions()
    {
        await using var scope = new ImageBatchTestScope(new() { StorageDirectory = "batches" });
        var controller = Controller(scope);
        var file = Form(ImageBatchTestScope.Png(SKColors.Red));
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await controller.Submit(new() { Files = [file] }));
        Assert.IsType<NotFoundResult>(controller.Get(id));
        Assert.IsType<NotFoundResult>(controller.Cancel(id));
        Assert.IsType<NotFoundResult>(await controller.Retry(id));
        Assert.IsType<NotFoundResult>(await controller.Download(id, 0));
        Assert.IsType<NotFoundResult>(controller.Delete(id));
        Assert.Equal(0, file.OpenCount);
        Assert.False(Directory.Exists(scope.StorageRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappedSubmissionFailure_AndMultipleDisposalFailuresPreserveOriginalCause(bool capacity)
    {
        await using var scope = new ImageBatchTestScope(new() { Enabled = true, StorageDirectory = "batches", MaximumJobs = 1 });
        if (capacity) await scope.SubmitAsync([1]);
        var primary = new InvalidDataException("source actual byte failure");
        var cleanup1 = new IOException("cleanup-one");
        var cleanup2 = new IOException("cleanup-two");
        var content = ImageBatchTestScope.Png(SKColors.Red);
        var first = Form(content, count => count == 1 ? new MemoryStream(content) : new ReadAndDisposalFailureStream(primary, cleanup1));
        var second = Form(content, count => count == 1 ? new MemoryStream(content) : new DisposalFailureStream(cleanup2, content));
        var error = await Assert.ThrowsAsync<AggregateException>(() => Controller(scope).Submit(new() { Files = [first, second] }));
        Assert.Equal(3, error.InnerExceptions.Count);
        if (capacity) Assert.IsType<ImageBatchCapacityException>(error.InnerExceptions[0]);
        else Assert.Same(primary, error.InnerExceptions[0]);
        Assert.Contains(cleanup1, error.InnerExceptions);
        Assert.Contains(cleanup2, error.InnerExceptions);
        Assert.All(first.Opened.Concat(second.Opened), s => Assert.False(s.CanRead));
    }

    [Fact]
    public async Task InputAcquisitionFailure_DisposesPreviouslyAcquiredInputAndPreservesBothErrors()
    {
        await using var scope = new ImageBatchTestScope();
        var content = ImageBatchTestScope.Png(SKColors.Red);
        var primary = new IOException("open-second");
        var cleanup = new IOException("close-first");
        var first = Form(content, count => count == 1 ? new MemoryStream(content) : new DisposalFailureStream(cleanup, content));
        var second = Form(content, count => count == 1 ? new MemoryStream(content) : throw primary);
        var error = await Assert.ThrowsAsync<AggregateException>(() => Controller(scope).Submit(new() { Files = [first, second] }));
        Assert.Equal(new Exception[] { primary, cleanup }, error.InnerExceptions);
        Assert.False(Directory.Exists(scope.StorageRoot));
        Assert.All(first.Opened, s => Assert.False(s.CanRead));
    }

    [Fact]
    public async Task Download_WhenJobCompletesBetweenObservationAndOpening_ReturnsItsMatchingStreamAndContentType()
    {
        await using var scope = new ImageBatchTestScope();
        var job = await scope.SubmitAsync([1]);
        await scope.SubmitAsync([255]);
        await scope.StartAsync();
        var active = await scope.Operation.NextAsync();
        var controller = Controller(scope);
        controller.HttpContext.Features.Set<IHttpRequestLifetimeFeature>(new CallbackLifetimeFeature(async () =>
        {
            active.Succeed([1, 2, 3]);
            _ = await scope.Operation.NextAsync().ConfigureAwait(false); // Single-worker next-job barrier, no time-based guessing.
            Assert.Equal(ImageBatchState.Succeeded, scope.Jobs.Get("alice", job.Id)!.State);
        }));
        var response = Assert.IsType<FileStreamResult>(await controller.Download(job.Id, 0));
        await using var download = response.FileStream;
        Assert.Equal("image/png", response.ContentType);
        using var bytes = new MemoryStream();
        await download.CopyToAsync(bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
    }

    private static ImageBatchesController Controller(ImageBatchTestScope scope, string? owner = "alice")
    {
        var context = new DefaultHttpContext();
        if (owner is not null) context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "test"));
        return new(scope.Jobs, new FileUploadValidator()) { ControllerContext = new() { HttpContext = context } };
    }
    private static TestFormFile Form(byte[] bytes, Func<int, Stream>? factory = null) => new(bytes, factory);

    private sealed class TestFormFile(byte[] bytes, Func<int, Stream>? factory) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => "form-data";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => bytes.Length;
        public string Name => "files";
        public string FileName => "image.png";
        internal int OpenCount { get; private set; }
        internal List<Stream> Opened { get; } = [];
        public Stream OpenReadStream()
        {
            OpenCount++;
            var stream = factory?.Invoke(OpenCount) ?? new MemoryStream(bytes);
            Opened.Add(stream);
            return stream;
        }
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class ReadAndDisposalFailureStream(Exception primary, Exception cleanup) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(primary);
        public override ValueTask DisposeAsync() { base.Dispose(true); return ValueTask.FromException(cleanup); }
    }
    private sealed class CallbackLifetimeFeature(Func<Task> callback) : IHttpRequestLifetimeFeature
    {
        private Func<Task>? _callback = callback;
        private CancellationToken _token;
        public CancellationToken RequestAborted
        {
            get { Interlocked.Exchange(ref _callback, null)?.Invoke().GetAwaiter().GetResult(); return _token; }
            set => _token = value;
        }
        public void Abort() => _token = new CancellationToken(true);
    }
}
