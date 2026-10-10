using System.Runtime.CompilerServices;
using System.Text.Json;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.WatermarkRemoval;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.WatermarkRemoval;

public sealed class WatermarkRemovalTests
{
    [Fact]
    public void Defaults_AreAutomaticRgbAndHalfProbabilityWithoutHeuristicFiltering()
    {
        Assert.Equal(0, (int)WatermarkRemovalMode.Automatic);
        var options = new PythonWatermarkDetectorOptions { PythonExecutablePath = "unused", ModelPath = "unused" };
        Assert.Equal(WatermarkNormalization.RgbZeroToOne, options.Normalization);
        Assert.Equal("rgb_0_1", options.Profile);
        Assert.Equal(0.5, options.Threshold);
    }

    [Fact]
    public void RequestValidation_RejectsAliasesMissingMaskAndWrongMode()
    {
        using var input = new MemoryStream([1]);
        using var image = new MemoryStream();
        using var mask = new MemoryStream();
        WatermarkRemover.ValidateRequest(input, image, mask, WatermarkRemovalMode.Automatic, null);
        Assert.Throws<ArgumentException>(() => WatermarkRemover.ValidateRequest(input, image, mask, WatermarkRemovalMode.Manual, null));
        Assert.Throws<ArgumentException>(() => WatermarkRemover.ValidateRequest(input, image, mask, WatermarkRemovalMode.Automatic, input));
        Assert.Throws<ArgumentException>(() => WatermarkRemover.ValidateRequest(input, image, image, WatermarkRemovalMode.Automatic, null));
        Assert.Throws<ArgumentException>(() => WatermarkRemover.ValidateRequest(input, input, mask, WatermarkRemovalMode.Automatic, null));
        Assert.Throws<ArgumentException>(() => WatermarkRemover.ValidateRequest(input, image, mask, (WatermarkRemovalMode)99, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DetectorOptions_RejectInvalidProbabilityThresholds(double threshold)
    {
        using var root = new TestRoot();
        var model = Path.Combine(root.Path, "model");
        File.WriteAllBytes(model, [0]);
        var options = new PythonWatermarkDetectorOptions
        {
            PythonExecutablePath = Environment.ProcessPath!, ModelPath = model, Threshold = threshold
        };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void DetectorOptions_RequireTrustedAbsoluteExecutableAndFiniteBudget()
    {
        using var root = new TestRoot();
        var model = Path.Combine(root.Path, "model");
        File.WriteAllBytes(model, [0]);
        var options = new PythonWatermarkDetectorOptions { PythonExecutablePath = Environment.ProcessPath!, ModelPath = model };
        options.Validate();
        Assert.Throws<ArgumentException>(() => (options with { PythonExecutablePath = "python" }).Validate());
        Assert.Throws<FileNotFoundException>(() => (options with { PythonExecutablePath = Path.Combine(root.Path, "missing.exe") }).Validate());
        Assert.Throws<FileNotFoundException>(() => (options with { ModelPath = Path.Combine(root.Path, "missing.pth") }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Timeout = Timeout.InfiniteTimeSpan }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Timeout = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { CpuThreads = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Normalization = (WatermarkNormalization)99 }).Validate());
    }

    [Fact]
    public void PackagedWorkerAndManifest_AreCopiedTransitivelyToConsumerOutput()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "media-inference");
        Assert.True(File.Exists(Path.Combine(directory, "watermark_detector.py")));
        Assert.True(File.Exists(Path.Combine(directory, "requirements.txt")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "modelmanifest.json")));
        Assert.Equal(WatermarkRemover.DetectorModelId, manifest.RootElement.GetProperty("modelId").GetString());
        Assert.Equal(WatermarkRemover.DetectorSha256, manifest.RootElement.GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(0, false)]
    public async Task Snapshot_EnforcesBoundedBytesLeavesSourceOpenAndWorkspaceCleanupIsOwned(int size, bool succeeds)
    {
        using var root = new TestRoot();
        var unrelated = Path.Combine(root.Path, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        var workspace = new RemovalWorkspace(root.Path);
        using var input = new MemoryStream(new byte[size]);
        try
        {
            var operation = RemovalWorkspace.SnapshotAsync(input, workspace.ImagePath, 3, default);
            if (succeeds)
            {
                await operation;
                Assert.Equal(3, new FileInfo(workspace.ImagePath).Length);
            }
            else await Assert.ThrowsAsync<InvalidDataException>(() => operation);
            Assert.True(input.CanRead);
        }
        finally { workspace.Delete(); }
        workspace.Delete();
        Assert.False(Directory.Exists(workspace.DirectoryPath));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task Snapshot_PreCanceled_LeavesSourceOpenAndCanBeCleaned()
    {
        using var root = new TestRoot();
        var workspace = new RemovalWorkspace(root.Path);
        using var input = new MemoryStream([1, 2, 3]);
        using var source = new CancellationTokenSource();
        source.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemovalWorkspace.SnapshotAsync(input, workspace.ImagePath, 3, source.Token));
            Assert.Equal(0, input.Position);
            Assert.True(input.CanRead);
        }
        finally { workspace.Delete(); }
    }

    [ModelFact]
    public async Task DefaultAutomaticWithoutDetector_IsConfigurationErrorAndManualNeedsNoPython()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path, automatic: false);
        using var input = new MemoryStream(Fixture("negative-scene.png"));
        using var imageOutput = new MemoryStream();
        using var maskOutput = new MemoryStream();
        await Assert.ThrowsAsync<InvalidOperationException>(() => remover.RemoveAsync(input, imageOutput, maskOutput));
        Assert.Equal(0, input.Position);
        Assert.Empty(Directory.GetDirectories(root.Path));
        using var manualMask = Mask(512, 384, (_, _) => false);

        var result = await remover.RemoveAsync(input, imageOutput, maskOutput, WatermarkRemovalMode.Manual, manualMask);

        Assert.Equal(WatermarkRemovalMode.Manual, result.Mode);
        Assert.Null(result.Detection);
        Assert.Equal(0, result.Image.SelectedPixelCount);
        Assert.Equal(manualMask.ToArray(), maskOutput.ToArray());
        Assert.True(input.CanRead && manualMask.CanRead && imageOutput.CanWrite && maskOutput.CanWrite);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task DefaultAutomatic_UsesRealDetectorAndMiganReturnsEditableMaskWithoutChangingOutside()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path);
        var original = Fixture("positive-repeated.png");
        using var input = new MemoryStream(original);
        using var imageOutput = new MemoryStream();
        using var maskOutput = new MemoryStream();

        var result = await remover.RemoveAsync(input, imageOutput, maskOutput);

        Assert.Equal(WatermarkRemovalMode.Automatic, result.Mode);
        Assert.NotNull(result.Detection);
        Assert.Equal(WatermarkRemover.DetectorModelId, result.Detection.ModelId);
        Assert.Equal("rgb_0_1", result.Detection.Profile);
        Assert.Equal(0.5, result.Detection.Threshold);
        Assert.True(result.Image.SelectedPixelCount > 0);
        Assert.True(result.Image.EditedPixelCount > 0);
        Assert.True(result.ReviewRecommended);
        Assert.NotEmpty(result.Warnings);
        using var baseline = await Baseline(original);
        using var actual = SKBitmap.Decode(imageOutput.ToArray());
        using var mask = SKBitmap.Decode(maskOutput.ToArray());
        var selected = 0;
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
        {
            var maskPixel = mask.GetPixel(x, y);
            Assert.True(maskPixel == SKColors.Black || maskPixel == SKColors.White);
            if (maskPixel == SKColors.Black) Assert.Equal(baseline.GetPixel(x, y), actual.GetPixel(x, y));
            else selected++;
        }
        Assert.Equal(selected, result.Image.SelectedPixelCount);
        Assert.Equal(selected, result.Detection.SelectedPixelCount);
        Assert.Equal(original, input.ToArray());
        Assert.True(input.CanRead && imageOutput.CanWrite && maskOutput.CanWrite);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task OrdinarySubtitleFalsePositive_IsExposedForReviewWithoutSampleSpecificFiltering()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path);
        using var input = new MemoryStream(Fixture("negative-subtitle.png"));
        using var image = new MemoryStream();
        using var mask = new MemoryStream();

        var result = await remover.RemoveAsync(input, image, mask);

        Assert.True(result.Image.SelectedPixelCount > 0);
        Assert.True(result.ReviewRecommended);
        Assert.Contains(result.Warnings, warning => warning.Contains("subtitles", StringComparison.Ordinal));
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task SceneWithoutWatermark_ReturnsEmptyMaskAndNormalizedIdentity()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path);
        var original = Fixture("negative-scene.png");
        using var input = new MemoryStream(original);
        using var image = new MemoryStream();
        using var mask = new MemoryStream();

        var result = await remover.RemoveAsync(input, image, mask);

        Assert.Equal(0, result.Image.SelectedPixelCount);
        Assert.Equal(0, result.Image.EditedPixelCount);
        using var baseline = await Baseline(original);
        using var actual = SKBitmap.Decode(image.ToArray());
        Assert.Equal(Pixels(baseline), Pixels(actual));
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task ActualPillowAndSkiaExifNormalization_UseTheSameMaskCoordinates()
    {
        using var root = new TestRoot();
        using var fixture = SKBitmap.Decode(Fixture("positive-repeated.png"));
        using var skImage = SKImage.FromBitmap(fixture);
        using var jpeg = skImage.Encode(SKEncodedImageFormat.Jpeg, 95);
        byte[] exif = [.. "Exif"u8, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        byte[] original = [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif, .. jpeg.ToArray().AsSpan(2)];
        await using var remover = await Create(root.Path, limits: new() { MaximumEncodedBytes = original.Length });
        using var input = new MemoryStream(original);
        using var image = new MemoryStream();
        using var mask = new MemoryStream();

        var result = await remover.RemoveAsync(input, image, mask);

        Assert.Equal(384, result.Image.Width);
        Assert.Equal(512, result.Image.Height);
        Assert.Equal(result.Image.Width, result.Detection!.Width);
        Assert.Equal(result.Image.Height, result.Detection.Height);
        using var baseline = await Baseline(original);
        using var actual = SKBitmap.Decode(image.ToArray());
        using var selected = SKBitmap.Decode(mask.ToArray());
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
            if (selected.GetPixel(x, y) == SKColors.Black) Assert.Equal(baseline.GetPixel(x, y), actual.GetPixel(x, y));
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task WrongCheckpoint_IsRejectedWithoutFakeFallbackAndArtifactsAreCleaned()
    {
        using var root = new TestRoot();
        var wrong = Path.Combine(root.Path, "wrong-model.pth");
        await File.WriteAllBytesAsync(wrong, [1, 2, 3]);
        await using var remover = await Create(root.Path, detectorModel: wrong);
        using var input = new MemoryStream(Fixture("positive-repeated.png"));
        using var image = new MemoryStream();
        using var mask = new MemoryStream();

        var error = await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(input, image, mask));

        Assert.Contains("SHA-256", error.StandardError, StringComparison.Ordinal);
        Assert.Equal(0, image.Length);
        Assert.Equal(0, mask.Length);
        Assert.True(input.CanRead && image.CanWrite && mask.CanWrite);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [DetectorFact]
    public async Task ActualWorker_TimeoutTerminatesAndCleansOwnedArtifacts()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path, timeout: TimeSpan.FromMilliseconds(10));
        using var input = new MemoryStream(Fixture("positive-repeated.png"));
        using var image = new MemoryStream();
        using var mask = new MemoryStream();

        await Assert.ThrowsAsync<TimeoutException>(() => remover.RemoveAsync(input, image, mask));

        Assert.Empty(Directory.GetDirectories(root.Path));
        Assert.True(input.CanRead && image.CanWrite && mask.CanWrite);
    }

    [DetectorFact]
    public async Task AutomaticCancellation_CleansArtifactsAndLeavesStreamsOpen()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path);
        using var input = new MemoryStream(Fixture("positive-repeated.png"));
        using var image = new MemoryStream();
        using var mask = new MemoryStream();
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remover.RemoveAsync(input, image, mask, cancellationToken: source.Token));

        Assert.Empty(Directory.GetDirectories(root.Path));
        Assert.Equal(0, image.Length);
        Assert.Equal(0, mask.Length);
        Assert.True(input.CanRead && image.CanWrite && mask.CanWrite);
    }

    [ModelFact]
    public async Task EmptyEncodedInput_IsRejectedAndRemovedFromOwnedWorkspace()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path, automatic: false);
        using var input = new MemoryStream();
        using var userMask = Mask(3, 3, (_, _) => false);
        using var image = new MemoryStream();
        using var mask = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => remover.RemoveAsync(input, image, mask, WatermarkRemovalMode.Manual, userMask));
        Assert.Empty(Directory.GetDirectories(root.Path));
        Assert.True(input.CanRead && userMask.CanRead && image.CanWrite && mask.CanWrite);
    }

    [ModelFact]
    public async Task QueuedCancellation_DoesNotReadInputsOrAffectActiveManualOperation()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path, automatic: false);
        using var first = new PausedReadStream(Fixture("negative-scene.png"));
        using var firstMask = Mask(512, 384, (_, _) => false);
        using var firstImage = new MemoryStream();
        using var firstMaskOutput = new MemoryStream();
        var active = remover.RemoveAsync(first, firstImage, firstMaskOutput, WatermarkRemovalMode.Manual, firstMask);
        using var next = new MemoryStream(Fixture("negative-scene.png"));
        using var nextImage = new MemoryStream();
        using var nextMask = new MemoryStream();
        using var cancel = new CancellationTokenSource();
        try
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var queued = remover.RemoveAsync(next, nextImage, nextMask, cancellationToken: cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, next.Position);
            Assert.Equal(0, nextImage.Length);
            Assert.Equal(0, nextMask.Length);
        }
        finally { first.Resume.TrySetResult(); }
        Assert.Equal(0, (await active).Image.SelectedPixelCount);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [ModelFact]
    public async Task Dispose_DrainsActiveOperationCleansWorkspaceAndRejectsQueuedWork()
    {
        using var root = new TestRoot();
        await using var remover = await Create(root.Path, automatic: false);
        using var first = new PausedReadStream(Fixture("negative-scene.png"));
        using var firstMask = Mask(512, 384, (_, _) => false);
        using var firstImage = new MemoryStream();
        using var firstMaskOutput = new MemoryStream();
        var active = remover.RemoveAsync(first, firstImage, firstMaskOutput, WatermarkRemovalMode.Manual, firstMask);
        using var next = new MemoryStream(Fixture("negative-scene.png"));
        using var nextImage = new MemoryStream();
        using var nextMask = new MemoryStream();
        try
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var disposal = remover.DisposeAsync().AsTask();
            var queued = remover.RemoveAsync(next, nextImage, nextMask);
            Assert.False(disposal.IsCompleted);
            first.Resume.TrySetResult();
            await active;
            await disposal;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            await remover.DisposeAsync();
            Assert.Equal(0, next.Position);
            Assert.Empty(Directory.GetDirectories(root.Path));
        }
        finally { first.Resume.TrySetResult(); }
    }

    private static Task<WatermarkRemover> Create(string temporary, bool automatic = true, string? detectorModel = null, TimeSpan? timeout = null, ImageLimits? limits = null)
    {
        var repair = new MiganInpaintingOptions { ModelPath = Environment.GetEnvironmentVariable(ModelFactAttribute.Variable)!, CpuThreads = 2 };
        if (limits is not null) repair = repair with { Limits = limits };
        return WatermarkRemover.CreateAsync(new()
        {
            Repair = repair,
            Detector = automatic ? new()
            {
                PythonExecutablePath = Environment.GetEnvironmentVariable(DetectorFactAttribute.PythonVariable)!,
                ModelPath = detectorModel ?? Environment.GetEnvironmentVariable(DetectorFactAttribute.ModelVariable)!,
                CpuThreads = 2, Timeout = timeout ?? TimeSpan.FromMinutes(2)
            } : null,
            TemporaryDirectory = temporary
        });
    }

    private static byte[] Fixture(string name, [CallerFilePath] string sourceFile = "") => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", name));
    private static async Task<SKBitmap> Baseline(byte[] original)
    {
        using var input = new MemoryStream(original);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output);
        return SKBitmap.Decode(output.ToArray());
    }
    private static MemoryStream Mask(int width, int height, Func<int, int, bool> select)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, select(x, y) ? SKColors.White : SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(encoded.ToArray());
    }
    private static SKColor[] Pixels(SKBitmap image) => Enumerable.Range(0, image.Height)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y))).ToArray();

    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-watermark-tests-" + Guid.NewGuid().ToString("N")));
        public TestRoot() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class PausedReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _paused;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_paused)
            {
                _paused = true;
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

public sealed class DetectorFactAttribute : FactAttribute
{
    internal const string PythonVariable = "MS_MEDIA_WATERMARK_PYTHON";
    internal const string ModelVariable = "MS_MEDIA_WATERMARK_MODEL";
    public DetectorFactAttribute()
    {
        if (new[] { ModelFactAttribute.Variable, PythonVariable, ModelVariable }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = $"Set {ModelFactAttribute.Variable}, {PythonVariable} and {ModelVariable} for real local automatic removal.";
    }
}
