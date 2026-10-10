using System.Text.Json;
using MS.Microservice.Media.Video;
using Xunit;

namespace MS.Microservice.Media.Tests.Video;

public sealed class VideoProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0, 320, 240)]
    [InlineData(90, 240, 320)]
    [InlineData(-90, 240, 320)]
    [InlineData(180, 320, 240)]
    [InlineData(450, 240, 320)]
    public void Parse_PreservesRationalFrameRateAndAccountsForRotation(int rotation, int width, int height)
    {
        using var json = JsonDocument.Parse($$$"""
            {"streams":[
              {"codec_type":"video","codec_name":"h264","width":320,"height":240,
               "avg_frame_rate":"30000/1001","side_data_list":[{"rotation":{{{rotation}}}}]},
              {"codec_type":"audio"}],"format":{"duration":"1.234"}}
            """);
        var info = VideoProcessor.ParseMetadata(json.RootElement);
        Assert.Equal((width, height), (info.Width, info.Height));
        Assert.Equal(30000d / 1001, info.FrameRate!.Value, 8);
        Assert.Equal(TimeSpan.FromSeconds(1.234), info.Duration);
        Assert.True(info.HasAudio);
        Assert.Equal("h264", info.Codec);
    }

    [Fact]
    public void Parse_IgnoresAttachedCoverAndUsesRotationTagWhenOtherSideDataExists()
    {
        using var json = JsonDocument.Parse("""
            {"streams":[
              {"codec_type":"video","width":1,"height":1,"disposition":{"attached_pic":1}},
              {"codec_type":"video","width":80,"height":60,"duration":"2.5","avg_frame_rate":"0/0",
               "side_data_list":[{"side_data_type":"other"}],"tags":{"rotate":"270"}}]}
            """);
        var info = VideoProcessor.ParseMetadata(json.RootElement);
        Assert.Equal((60, 80), (info.Width, info.Height));
        Assert.Equal(TimeSpan.FromSeconds(2.5), info.Duration);
        Assert.Null(info.FrameRate);
        Assert.False(info.HasAudio);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"streams\":[]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"audio\"}]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"width\":0,\"height\":20}]}")]
    public void Parse_RejectsMissingVideoOrInvalidDimensions(string metadata)
    {
        using var json = JsonDocument.Parse(metadata);
        Assert.ThrowsAny<Exception>(() => VideoProcessor.ParseMetadata(json.RootElement));
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Parse_UnknownDurationRemainsUnknown(string duration)
    {
        using var json = JsonDocument.Parse($$$"""
            {"streams":[{"codec_type":"video","width":10,"height":20}],"format":{"duration":"{{{duration}}}"}}
            """);
        Assert.Null(VideoProcessor.ParseMetadata(json.RootElement).Duration);
    }

    [Fact]
    public async Task Probe_PreCancellationAndMissingFileDoNotStartConfiguredExecutable()
    {
        var processor = new VideoProcessor(new FfmpegOptions { FfprobePath = "not-installed-ffprobe" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProbeAsync("input.mp4", new CancellationToken(true)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => processor.ProbeAsync(Path.Combine(_root, "missing.mp4")));
    }

    [FfmpegFact]
    public async Task Probe_InspectsRealVideoIncludingPathWithSpacesAndUnicode()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "视频 with spaces.mp4");
        var runner = new MediaProcessRunner(TimeSpan.FromSeconds(30));
        await runner.RunAsync("ffmpeg", ["-v", "error", "-nostdin", "-f", "lavfi", "-i",
            "color=c=red:s=80x60:r=25", "-t", "0.4", "-c:v", "libx264", "-pix_fmt", "yuv420p", file], default);

        var info = await new VideoProcessor().ProbeAsync(file);
        Assert.Equal((80, 60), (info.Width, info.Height));
        Assert.Equal("h264", info.Codec);
        Assert.Equal(25, info.FrameRate);
        Assert.InRange(info.Duration!.Value.TotalSeconds, .39, .45);
        Assert.False(info.HasAudio);
    }

    [FfmpegFact]
    public async Task Runner_PropagatesRealExitErrors()
    {
        var error = await Assert.ThrowsAsync<MediaProcessException>(() =>
            new MediaProcessRunner(TimeSpan.FromSeconds(10)).RunAsync("ffmpeg", ["-definitely-invalid-option"], default));
        Assert.NotEqual(0, error.ExitCode);
        Assert.NotEmpty(error.StandardError);
    }

    [FfmpegFact]
    public async Task Runner_TimeoutAndCallerCancellationTerminateChild()
    {
        string[] arguments = ["-v", "error", "-nostdin", "-re", "-f", "lavfi", "-i",
            "testsrc=size=32x32:rate=10", "-f", "null", "-"];
        await Assert.ThrowsAsync<TimeoutException>(() =>
            new MediaProcessRunner(TimeSpan.FromMilliseconds(300)).RunAsync("ffmpeg", arguments, default));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MediaProcessRunner(TimeSpan.FromSeconds(30)).RunAsync("ffmpeg", arguments, cancellation.Token));
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MEDIA_FFMPEG_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_MEDIA_FFMPEG_TESTS=true with ffmpeg/ffprobe installed to run real media integration tests.";
    }
}
