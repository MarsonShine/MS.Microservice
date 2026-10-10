using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;
using MS.Microservice.Media.Inference.WatermarkRemoval;
using MS.Microservice.Media.Inference.Tests.WatermarkRemoval;
using MS.Microservice.Media.Video;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.VideoWatermarkRemoval;

public sealed class AutomaticVideoWatermarkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DetectorSettings_RejectInvalidUncalibratedThreshold(double threshold)
    {
        using var root = new TestRoot();
        var model = Path.Combine(root.Path, "checkpoint");
        File.WriteAllBytes(model, [0]);
        Assert.Throws<ArgumentException>(() => new VideoWatermarkDetectorOptions { ModelPath = model, Threshold = threshold }.Validate());
    }

    [Fact]
    public void DetectorSettings_DefaultToSameImageProtocolAndRequireExistingModel()
    {
        using var root = new TestRoot();
        var model = Path.Combine(root.Path, "checkpoint");
        File.WriteAllBytes(model, [0]);
        var options = new VideoWatermarkDetectorOptions { ModelPath = model };
        options.Validate();
        Assert.Equal(WatermarkNormalization.RgbZeroToOne, options.Normalization);
        Assert.Equal("rgb_0_1", options.Profile);
        Assert.Equal(.5, options.Threshold);
        Assert.Throws<ArgumentException>(() => (options with { Normalization = (WatermarkNormalization)99 }).Validate());
        Assert.Throws<FileNotFoundException>(() => (options with { ModelPath = Path.Combine(root.Path, "missing") }).Validate());
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "media-inference", "watermark_model.py")));
    }

    [Fact]
    public async Task DefaultAutomatic_MissingDetectorPreCancellationAndEmptyInputNeverPublishOrStartBackend()
    {
        using var root = new TestRoot();
        var model = Path.Combine(root.Path, "dummy-model");
        File.WriteAllBytes(model, [0]);
        var options = new VideoWatermarkRemovalOptions { PythonExecutablePath = Environment.ProcessPath!, MiganModelPath = model };
        await using (var remover = new VideoWatermarkRemover(options))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => remover.RemoveAsync("missing-input", Path.Combine(root.Path, "result")));
            Assert.Contains("repeated-text", error.Message, StringComparison.Ordinal);
            var request = new VideoWatermarkRemovalRequest { RectangleKeyframes = [] };
            await Assert.ThrowsAsync<ArgumentException>(() => remover.RemoveAsync("missing-input", Path.Combine(root.Path, "result"), request));
            await Assert.ThrowsAsync<ArgumentException>(() => remover.RemoveAsync("missing-input", Path.Combine(root.Path, "result"), new() { RectangleKeyframes = [] }));
            await Assert.ThrowsAsync<ArgumentException>(() => remover.RemoveAsync("missing-input", Path.Combine(root.Path, "result"), new() { RectangleKeyframes = [] }, CancellationToken.None));
        }
        await using (var remover = new VideoWatermarkRemover(options with { Detector = new() { ModelPath = model } }))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remover.RemoveAsync("missing-input", Path.Combine(root.Path, "result"), cancellation.Token));
            var input = Path.Combine(root.Path, "empty-input");
            File.WriteAllBytes(input, []);
            await Assert.ThrowsAsync<InvalidDataException>(() => remover.RemoveAsync(input, Path.Combine(root.Path, "result")));
        }
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [VideoDetectorTheory]
    [InlineData("moving", true)]
    [InlineData("fixed", true)]
    [InlineData("scene", false)]
    public async Task DefaultAutomatic_RealPerFrameDetectionAndRepairPreserveVfrAudioAndOriginal(string name, bool expectsMask)
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        var input = Path.Combine(root.Path, $"auto-{name}.mkv");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
        await using var remover = new VideoWatermarkRemover(Options());
        var result = await remover.RemoveAsync(input, Path.Combine(root.Path, "result"));
        Assert.Equal(WatermarkRemovalMode.Automatic, result.Mode);
        Assert.Equal(4, result.FrameCount);
        Assert.Equal(512, result.Width);
        Assert.Equal(384, result.Height);
        Assert.Equal(expectsMask, result.SelectedPixelCount > 0);
        Assert.Equal(expectsMask, result.EditedPixelCount > 0);
        if (expectsMask) Assert.True(result.FlowContributedPixelCount > 0);
        else Assert.Equal(0, result.FlowContributedPixelCount);
        var detection = Assert.IsType<VideoWatermarkDetectionResult>(result.Detection);
        Assert.Equal(WatermarkRemover.DetectorModelId, detection.ModelId);
        Assert.Equal(WatermarkRemover.DetectorSha256, detection.Sha256);
        Assert.Equal("rgb_0_1", detection.Profile);
        Assert.Equal(.5, detection.Threshold);
        Assert.Equal(4, detection.FrameCount);
        Assert.InRange(detection.MaximumProbability, 0, 1);
        Assert.InRange(detection.MeanProbability, 0, 1);
        Assert.True(detection.InferenceSeconds > 0);
        Assert.True(result.ReviewRecommended);
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
        var audio = Assert.Single(result.Audio);
        Assert.Equal(24000, audio.Samples);
        Assert.Equal(.025, audio.FirstSeconds, 6);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "frame-metadata.json")));
        var frames = json.RootElement.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal([0, 40, 140, 300], frames.Select(frame => frame.GetProperty("outputPtsMilliseconds").GetInt32()));
        Assert.Equal(result.SelectedPixelCount, frames.Sum(frame => frame.GetProperty("detection").GetProperty("selectedPixelCount").GetInt64()));
        Assert.All(frames, frame => Assert.InRange(frame.GetProperty("detection").GetProperty("meanProbability").GetDouble(), 0, 1));
        Assert.Equal([result.OutputDirectory], Directory.GetDirectories(root.Path));
    }

    [VideoDetectorFact]
    public async Task OrdinarySubtitleFalsePositive_RemainsVisibleWithWarningsAndEditablePreview()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(Options());
        var result = await remover.RemoveAsync(Path.Combine(root.Path, "auto-subtitle.mkv"), Path.Combine(root.Path, "result"));
        Assert.True(result.SelectedPixelCount > 0);
        Assert.True(result.EditedPixelCount > 0);
        Assert.True(result.ReviewRecommended);
        Assert.Contains(result.Warnings, warning => warning.Contains("ordinary text", StringComparison.Ordinal) && warning.Contains("subtitles", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "mask-preview.mkv")));
        Assert.NotNull(result.Detection);
    }

    [VideoDetectorFact]
    public async Task AutomaticQuarterTurn_UsesNormalizedCoordinatesAndExplicitImageNetProfile()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        var options = Options();
        await using var remover = new VideoWatermarkRemover(options with { Detector = options.Detector! with { Normalization = WatermarkNormalization.ImageNet, Threshold = .75 } });
        var result = await remover.RemoveAsync(Path.Combine(root.Path, "auto-rotation90.mov"), Path.Combine(root.Path, "result"));
        Assert.Equal(384, result.Width);
        Assert.Equal(512, result.Height);
        Assert.Equal(4, result.FrameCount);
        Assert.Equal("imagenet", result.Detection!.Profile);
        Assert.Equal(.75, result.Detection.Threshold);
        Assert.Equal(24000, Assert.Single(result.Audio).Samples);
    }

    [VideoDetectorFact]
    public async Task InvalidDetectorTimeoutAndInflightCancellation_HaveNoFallbackOrPartialPublication()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        var input = Path.Combine(root.Path, "auto-fixed.mkv");
        var bad = Path.Combine(root.Path, "bad-checkpoint");
        File.WriteAllBytes(bad, [0, 1, 2]);
        var options = Options();
        await using (var remover = new VideoWatermarkRemover(options with { Detector = options.Detector! with { ModelPath = bad } }))
        {
            var error = await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(input, Path.Combine(root.Path, "bad")));
            Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
        }
        await using (var remover = new VideoWatermarkRemover(options with { ProcessTimeout = TimeSpan.FromMilliseconds(50) }))
            await Assert.ThrowsAsync<TimeoutException>(() => remover.RemoveAsync(input, Path.Combine(root.Path, "timeout")));
        await using (var remover = new VideoWatermarkRemover(options))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remover.RemoveAsync(input, Path.Combine(root.Path, "cancelled"), cancellation.Token));
        }
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [VideoDetectorFact]
    public Task SharedCheckpoint_IsLoadedOnceFromVerifiedBytesDespitePathReplacement() =>
        VideoWatermarkRemovalTests.TestPython(TestFile("detector_contracts.py"), Path.Combine(AppContext.BaseDirectory, "media-inference"),
            Environment.GetEnvironmentVariable(DetectorFactAttribute.ModelVariable)!, ImageFixtures());

    private static VideoWatermarkRemovalOptions Options() => new()
    {
        PythonExecutablePath = Environment.GetEnvironmentVariable(VideoModelFactAttribute.PythonVariable)!,
        MiganModelPath = Environment.GetEnvironmentVariable(ModelFactAttribute.Variable)!, CpuThreads = 2,
        Detector = new() { ModelPath = Environment.GetEnvironmentVariable(DetectorFactAttribute.ModelVariable)! }
    };
    private static Task Fixtures(string directory) => VideoWatermarkRemovalTests.TestPython(TestFile("video_fixtures.py"), directory, "--automatic-fixtures", ImageFixtures());
    private static string TestFile(string name, [CallerFilePath] string sourceFile = "") => Path.Combine(Path.GetDirectoryName(sourceFile)!, name);
    private static string ImageFixtures([CallerFilePath] string sourceFile = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "WatermarkRemoval", "Fixtures"));
    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-auto-video-tests-" + Guid.NewGuid().ToString("N"));
        public TestRoot() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

public sealed class VideoDetectorFactAttribute : FactAttribute
{
    public VideoDetectorFactAttribute()
    {
        if (new[] { ModelFactAttribute.Variable, VideoModelFactAttribute.PythonVariable, DetectorFactAttribute.ModelVariable }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Configure all three existing image-model/Python variables for real automatic video removal.";
    }
}
public sealed class VideoDetectorTheoryAttribute : TheoryAttribute
{
    public VideoDetectorTheoryAttribute()
    {
        if (new[] { ModelFactAttribute.Variable, VideoModelFactAttribute.PythonVariable, DetectorFactAttribute.ModelVariable }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Configure all three existing image-model/Python variables for real automatic video removal.";
    }
}
