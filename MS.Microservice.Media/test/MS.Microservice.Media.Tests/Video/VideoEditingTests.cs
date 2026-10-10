using System.ComponentModel;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;
using Xunit;

namespace MS.Microservice.Media.Tests.Video;

public sealed class VideoEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoEditingTests", Guid.NewGuid().ToString("N"));

    [FfmpegTheory]
    [InlineData(".mp4", "h264")]
    [InlineData(".mkv", "h264")]
    [InlineData(".webm", "vp9")]
    public async Task Process_TranscodesAndPreservesVideoAndAudio(string extension, string codec)
    {
        var input = await SourceAsync(withAudio: true);
        var output = Path.Combine(_root, "输出 with spaces" + extension);
        var result = await new VideoProcessor().ProcessAsync(input, output);
        Assert.Equal((80, 60), (result.Width, result.Height));
        Assert.Equal(codec, result.Codec);
        Assert.True(result.HasAudio);
        Assert.InRange(result.Duration!.Value.TotalSeconds, .55, .75);
        Assert.True(File.Exists(input));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task Process_ComposesTrimCropRotationFlipsAndContainResize()
    {
        var input = await SourceAsync(withAudio: true);
        var output = Path.Combine(_root, "edited.mp4");
        var result = await new VideoProcessor().ProcessAsync(input, output, new VideoEditOptions
        {
            Start = TimeSpan.FromSeconds(.12), Duration = TimeSpan.FromSeconds(.24),
            Crop = new(1, 1, 50, 30), RotationDegrees = 90, FlipHorizontal = true, FlipVertical = true,
            Resize = new(40, 40), ResizeMode = ImageResizeMode.Contain
        });
        Assert.Equal((24, 40), (result.Width, result.Height));
        Assert.InRange(result.Duration!.Value.TotalSeconds, .20, .40);
        Assert.True(result.HasAudio);
    }

    [FfmpegTheory]
    [InlineData(ImageResizeMode.Cover)]
    [InlineData(ImageResizeMode.Stretch)]
    public async Task Process_ExactResizeModesProduceRequestedEvenBox(ImageResizeMode mode)
    {
        var result = await new VideoProcessor().ProcessAsync(await SourceAsync(), Path.Combine(_root, "resized.mp4"),
            new VideoEditOptions { Resize = new(40, 40), ResizeMode = mode });
        Assert.Equal((40, 40), (result.Width, result.Height));
        Assert.False(result.HasAudio);
    }

    [FfmpegFact]
    public async Task Process_PadsOddCropDimensionsForYuv420InsteadOfDiscardingEdgePixels()
    {
        var result = await new VideoProcessor().ProcessAsync(await SourceAsync(), Path.Combine(_root, "odd.mp4"),
            new VideoEditOptions { Crop = new(1, 1, 31, 29) });
        Assert.Equal((32, 30), (result.Width, result.Height));
    }

    [FfmpegFact]
    public async Task Process_DurationBeyondEndClampsAndPreservesSilentInput()
    {
        var result = await new VideoProcessor().ProcessAsync(await SourceAsync(), Path.Combine(_root, "clamped.mp4"),
            new VideoEditOptions { Start = TimeSpan.FromSeconds(.2), Duration = TimeSpan.FromSeconds(10) });
        Assert.False(result.HasAudio);
        Assert.InRange(result.Duration!.Value.TotalSeconds, .35, .5);
    }

    [FfmpegFact]
    public async Task Process_InvalidCropOrStartDoesNotPublish()
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "invalid.mp4");
        await Assert.ThrowsAsync<ArgumentException>(() => new VideoProcessor().ProcessAsync(input, output,
            new VideoEditOptions { Crop = new(79, 0, 2, 1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => new VideoProcessor().ProcessAsync(input, output,
            new VideoEditOptions { Start = TimeSpan.FromSeconds(1) }));
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task Process_WhenExecutableCannotStart_RemovesOnlyOwnedTemporaryOutput()
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "failed.mp4");
        var existing = Path.Combine(_root, "keep.txt");
        await File.WriteAllTextAsync(existing, "keep");
        var processor = new VideoProcessor(new FfmpegOptions { FfmpegPath = Path.Combine(_root, "missing-ffmpeg.exe") });
        await Assert.ThrowsAsync<Win32Exception>(() => processor.ProcessAsync(input, output));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Process_DoesNotOverwriteExistingDestinationOrSamePath()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "existing.mp4");
        await File.WriteAllTextAsync(file, "keep");
        var processor = new VideoProcessor(new FfmpegOptions { FfprobePath = "not-installed" });
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(file, file));
        await Assert.ThrowsAsync<IOException>(() => processor.ProcessAsync("not-installed.mp4", file));
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Process_RejectsInvalidOptionsBeforeStartingProcess()
    {
        var processor = new VideoProcessor(new FfmpegOptions { FfprobePath = "not-installed" });
        var output = Path.Combine(_root, "out.mp4");
        VideoEditOptions[] invalid =
        [
            new() { Start = TimeSpan.FromTicks(-1) }, new() { Duration = TimeSpan.Zero },
            new() { RotationDegrees = 45 }, new() { ConstantRateFactor = 52 },
            new() { Speed = (VideoEncodingSpeed)99 }, new() { MaximumPixels = 0 }
        ];
        foreach (var options in invalid)
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync("input.mp4", output, options));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync("input.mp4", output,
            new VideoEditOptions { Resize = new(0, 1) }));
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ProcessAsync("input.mp4", Path.Combine(_root, "out.unknown")));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Process_PreCancelledDoesNotStartOrCreateOutput()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoProcessor().ProcessAsync(
            "input.mp4", Path.Combine(_root, "cancel.mp4"), cancellationToken: new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(101, 100, 101, 11_000)]
    [InlineData(100, 101, 101, 11_000)]
    [InlineData(101, 101, 102, 10_201)]
    public async Task Process_RejectsPostPaddingBudgetOverrunsBeforeStartingProcess(
        int width, int height, int maximumDimension, long maximumPixels)
    {
        var processor = new VideoProcessor(new FfmpegOptions { FfprobePath = "not-installed" });
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync("input.mp4",
            Path.Combine(_root, "padded.mp4"), new VideoEditOptions
            {
                Resize = new(width, height), MaximumDimension = maximumDimension, MaximumPixels = maximumPixels
            }));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(99, 99)]
    [InlineData(99, 100)]
    public async Task Process_AcceptsPaddingWithinExactBudget(int width, int height)
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => new VideoProcessor().ProcessAsync(
            Path.Combine(_root, "missing.mp4"), Path.Combine(_root, "out.mp4"), new VideoEditOptions
            {
                Resize = new(width, height), MaximumDimension = 100, MaximumPixels = 10_000
            }));
        Assert.False(Directory.Exists(_root));
    }

    private async Task<string> SourceAsync(bool withAudio = false)
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "input.mp4");
        var arguments = new List<string> { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc=size=80x60:rate=25" };
        if (withAudio) arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]);
        arguments.AddRange(["-t", "0.6", "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (withAudio) arguments.AddRange(["-c:a", "aac"]);
        arguments.Add(input);
        await new MediaProcessRunner(TimeSpan.FromSeconds(30)).RunAsync("ffmpeg", arguments, default);
        return input;
    }

    private void AssertNoTemporaryFiles() => Assert.Empty(Directory.GetFiles(_root, ".video-*"));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoEditingTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MEDIA_FFMPEG_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_MEDIA_FFMPEG_TESTS=true with ffmpeg/ffprobe installed to run real media integration tests.";
    }
}
