using System.ComponentModel;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Video;

public sealed class VideoWatermarkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoWatermarkTests", Guid.NewGuid().ToString("N"));
    private static readonly WatermarkPlacement Placement = new() { Anchor = WatermarkAnchor.TopLeft, MarginX = 0, MarginY = 0, Opacity = 1 };

    [FfmpegTheory]
    [InlineData(".mp4", "h264")]
    [InlineData(".mkv", "h264")]
    [InlineData(".webm", "vp9")]
    public async Task Logo_UsesSharedPlacementAndKeepsAudioAndDuration(string extension, string codec)
    {
        var input = await SourceAsync(audio: true);
        var output = Path.Combine(_root, "水印 ' [x]; video" + extension);
        using var logo = Logo();
        var info = await new VideoProcessor().AddLogoWatermarkAsync(input, output, logo, new(16, 16), new() { Placement = Placement });
        Assert.True(logo.CanRead);
        Assert.True(info.HasAudio);
        Assert.Equal(codec, info.Codec);
        Assert.Equal((80, 60), (info.Width, info.Height));
        Assert.InRange(info.Duration!.Value.TotalSeconds, .79, 1.0);
        using var frame = await FrameAsync(output, .3);
        AssertGreen(frame.GetPixel(8, 8));
        AssertBlue(frame.GetPixel(60, 30));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task Logo_StartIsInclusiveAndEndIsExclusiveOnVideoTimeline()
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "timed.mkv");
        using var logo = Logo();
        await new VideoProcessor().AddLogoWatermarkAsync(input, output, logo, options: new()
        { Placement = Placement, Start = TimeSpan.FromSeconds(.2), End = TimeSpan.FromSeconds(.4) });
        foreach (var time in new[] { 0d, .1, .4, .7 })
        {
            using var frame = await FrameAsync(output, time);
            AssertBlue(frame.GetPixel(8, 8));
        }
        foreach (var time in new[] { .2, .3 })
        {
            using var frame = await FrameAsync(output, time);
            AssertGreen(frame.GetPixel(8, 8));
        }
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task Text_RendersVisibleGlyphsWithSharedRendererOnSilentVideo()
    {
        var output = Path.Combine(_root, "text.mp4");
        var info = await new VideoProcessor().AddTextWatermarkAsync(await SourceAsync(), output,
            new("TEST") { FontSize = 14 }, new() { Placement = Placement });
        Assert.False(info.HasAudio);
        using var frame = await FrameAsync(output, .2);
        Assert.True(Enumerable.Range(0, 30).SelectMany(y => Enumerable.Range(0, 60).Select(x => frame.GetPixel(x, y)))
            .Count(color => color.Red > 120 && color.Green > 120) > 30);
    }

    [FfmpegFact]
    public async Task Logo_ZeroOpacityLeavesBlueAndEndBeyondDurationIsClampedByInput()
    {
        var output = Path.Combine(_root, "transparent.mp4");
        using var logo = Logo();
        var info = await new VideoProcessor().AddLogoWatermarkAsync(await SourceAsync(), output, logo,
            options: new() { Placement = Placement with { Opacity = 0 }, End = TimeSpan.FromSeconds(10) });
        using var frame = await FrameAsync(output, .3);
        AssertBlue(frame.GetPixel(8, 8));
        Assert.InRange(info.Duration!.Value.TotalSeconds, .79, .85);
    }

    [FfmpegTheory]
    [InlineData(90, 60, 80)]
    [InlineData(180, 80, 60)]
    [InlineData(270, 60, 80)]
    public async Task Logo_RotationMetadataUsesOrientedRasterAndClearsRotation(int rotation, int width, int height)
    {
        var input = await SourceAsync();
        var rotated = Path.Combine(_root, "rotated.mp4");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-display_rotation", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", input, "-c", "copy", rotated], default);
        var processor = new VideoProcessor();
        Assert.Equal(rotation, (await processor.ProbeAsync(rotated)).RotationDegrees);
        var output = Path.Combine(_root, "upright.mp4");
        using var logo = Logo();
        var info = await processor.AddLogoWatermarkAsync(rotated, output, logo, options: new() { Placement = Placement });
        Assert.Equal((width, height, 0), (info.Width, info.Height, info.RotationDegrees));
        using var frame = await FrameAsync(output, .2);
        AssertGreen(frame.GetPixel(8, 8));
    }

    [FfmpegTheory]
    [InlineData(90, 60, 80)]
    [InlineData(180, 80, 60)]
    [InlineData(270, 60, 80)]
    public async Task ExistingProcess_AlsoClearsAppliedRotation(int rotation, int width, int height)
    {
        var input = await SourceAsync();
        var rotated = Path.Combine(_root, "rotated.mp4");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-display_rotation", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", input, "-c", "copy", rotated], default);
        var processor = new VideoProcessor();
        Assert.Equal(rotation, (await processor.ProbeAsync(rotated)).RotationDegrees);
        var result = await processor.ProcessAsync(rotated, Path.Combine(_root, "processed.mp4"));
        Assert.Equal((width, height, 0), (result.Width, result.Height, result.RotationDegrees));
    }

    [FfmpegTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidTimelineOrRaster_DoesNotPublish(int variant)
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "invalid.mp4");
        var options = variant switch
        {
            0 => new VideoWatermarkOptions { Start = TimeSpan.FromSeconds(2), Placement = Placement },
            1 => new VideoWatermarkOptions { Start = TimeSpan.FromSeconds(.2), End = TimeSpan.FromSeconds(.2), Placement = Placement },
            _ => new VideoWatermarkOptions { Placement = Placement with { Limits = new() { MaximumPixels = 4799 } } }
        };
        await Assert.ThrowsAnyAsync<Exception>(() => new VideoProcessor().AddTextWatermarkAsync(input, output, new("X"), options));
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    [FfmpegTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderingOrEncoderFailure_RemovesOnlyOwnedStaging(bool missingEncoder)
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "failed.mp4");
        var keep = Path.Combine(_root, ".video-user.mp4.png");
        await File.WriteAllTextAsync(keep, "keep");
        var processor = new VideoProcessor(missingEncoder ? new() { FfmpegPath = Path.Combine(_root, "missing.exe") } : null);
        var text = new TextWatermark("X") { FontSize = 12, FontFile = missingEncoder ? null : Path.Combine(_root, "missing.ttf") };
        if (missingEncoder)
            await Assert.ThrowsAsync<Win32Exception>(() => processor.AddTextWatermarkAsync(input, output, text, new() { Placement = Placement }));
        else
            await Assert.ThrowsAnyAsync<Exception>(() => processor.AddTextWatermarkAsync(input, output, text, new() { Placement = Placement }));
        Assert.False(File.Exists(output));
        Assert.Equal(new[] { keep }, Directory.GetFiles(_root, ".video-*"));
        Assert.Equal("keep", await File.ReadAllTextAsync(keep));
    }

    [Fact]
    public async Task CancellationAndExistingOutputFailBeforeInputAccess()
    {
        var processor = new VideoProcessor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.AddTextWatermarkAsync("missing.mp4", Path.Combine(_root, "cancel.mp4"),
            new("X"), cancellationToken: new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "existing.mp4");
        await File.WriteAllTextAsync(existing, "keep");
        await Assert.ThrowsAsync<IOException>(() => processor.AddTextWatermarkAsync("missing.mp4", existing, new("X")));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
    }

    private async Task<string> SourceAsync(bool audio = false)
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "source.mp4");
        var arguments = new List<string> { "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=80x60:r=10:d=0.8" };
        if (audio) arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.8"]);
        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) arguments.AddRange(["-c:a", "aac"]);
        arguments.Add(input);
        await Runner().RunAsync("ffmpeg", arguments, default);
        return input;
    }

    private static MemoryStream Logo()
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.Lime);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(bytes.ToArray());
    }

    private async Task<SKBitmap> FrameAsync(string input, double seconds)
    {
        var output = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".png");
        await new VideoProcessor().CaptureFrameAsync(input, output, TimeSpan.FromSeconds(seconds));
        return SKBitmap.Decode(File.ReadAllBytes(output));
    }

    private static void AssertGreen(SKColor color) { Assert.True(color.Green > 170 && color.Red < 80 && color.Blue < 80, color.ToString()); }
    private static void AssertBlue(SKColor color) { Assert.True(color.Blue > 170 && color.Red < 80 && color.Green < 80, color.ToString()); }
    private static MediaProcessRunner Runner() => new(TimeSpan.FromSeconds(30));
    private void AssertNoTemporaryFiles() => Assert.Empty(Directory.GetFiles(_root, ".video-*"));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoWatermarkTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
