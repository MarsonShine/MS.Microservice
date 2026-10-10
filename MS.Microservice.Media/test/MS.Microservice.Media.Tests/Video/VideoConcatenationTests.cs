using System.Globalization;
using System.Text.Json;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Video;

public sealed class VideoConcatenationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoConcatenationTests", Guid.NewGuid().ToString("N"));

    [FfmpegTheory]
    [InlineData(".mp4", "h264")]
    [InlineData(".mkv", "h264")]
    [InlineData(".webm", "vp9")]
    public async Task MixedSizesRatesCodecsAndSilentSegmentsNormalizeInDeclaredOrder(string extension, string codec)
    {
        var red = await SourceAsync("red", 80, 60, 10, .4, audioDuration: .4);
        var blue = await SourceAsync("blue", 120, 80, 20, .4, extension: ".mp4");
        var output = Path.Combine(_root, "合并 with 'quotes;[x]" + extension);
        var result = await new VideoProcessor().ConcatenateAsync([red, blue], output,
            new() { Canvas = new(100, 100), FrameRate = 25 });
        Assert.Equal((100, 100), (result.Width, result.Height));
        Assert.Equal(codec, result.Codec);
        Assert.Equal(25, result.FrameRate);
        Assert.InRange(result.Duration!.Value.TotalSeconds, .79, .86);
        Assert.True(result.HasAudio);
        var first = await FramePixelAsync(output, .2, 50, 50);
        var second = await FramePixelAsync(output, .6, 50, 50);
        Assert.True(first.Red > 220 && first.Blue < 25);
        Assert.True(second.Blue > 220 && second.Red < 25);
        Assert.True((await FramePixelAsync(output, .2, 50, 1)).Red < 20);
        var (audio, samples) = await SamplesAsync(output);
        Assert.Equal((48_000, 2), (audio.SampleRate, audio.Channels));
        Assert.InRange(Tone(samples, .1, .3), 430, 450);
        Assert.True(Energy(samples, .55, .7) < 200);
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task AllSilentSingleAndRepeatedInputsRetainTheirInverseContractsAndExactConfiguredCountBoundary()
    {
        var red = await SourceAsync("red", 80, 60, 10, .2);
        var processor = new VideoProcessor();
        var single = await processor.ConcatenateAsync([red], Path.Combine(_root, "single.mp4"));
        Assert.False(single.HasAudio);
        Assert.Equal((80, 60), (single.Width, single.Height));
        var output = Path.Combine(_root, "repeated.mp4");
        var repeated = await processor.ConcatenateAsync([red, red, red, red], output, new() { MaximumInputs = 4 });
        Assert.False(repeated.HasAudio);
        Assert.InRange(repeated.Duration!.Value.TotalSeconds, .79, .81);
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractAudioAsync(output, Path.Combine(_root, "silent.wav")));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task ContainerLongerThanVideoHoldsLastFrameAndDoesNotAdvanceFollowingClip()
    {
        var red = await SourceAsync("red", 80, 60, 10, .3, audioDuration: .8);
        var blue = await SourceAsync("blue", 80, 60, 20, .4, audioDuration: .4, frequency: 880);
        var output = Path.Combine(_root, "video-short.mp4");
        var result = await new VideoProcessor().ConcatenateAsync([red, blue], output, new() { FrameRate = 10 });
        Assert.InRange(result.Duration!.Value.TotalSeconds, 1.19, 1.25);
        Assert.True((await FramePixelAsync(output, .6, 40, 30)).Red > 220);
        Assert.True((await FramePixelAsync(output, .9, 40, 30)).Blue > 220);
        var (_, samples) = await SamplesAsync(output);
        Assert.InRange(Tone(samples, .45, .65), 430, 450);
        Assert.InRange(Tone(samples, .9, 1.1), 870, 890);
    }

    [FfmpegFact]
    public async Task ShortAudioGetsSilenceAndFractionalSegmentDurationRoundsOnTheSameFrameGrid()
    {
        var red = await SourceAsync("red", 80, 60, 20, .45, audioDuration: .2);
        var blue = await SourceAsync("blue", 80, 60, 10, .2, audioDuration: .2, frequency: 880);
        var output = Path.Combine(_root, "quantized.mp4");
        var result = await new VideoProcessor().ConcatenateAsync([red, blue], output, new() { FrameRate = 10 });
        Assert.InRange(result.Duration!.Value.TotalSeconds, .69, .75);
        Assert.True((await FramePixelAsync(output, .4, 40, 30)).Red > 220);
        Assert.True((await FramePixelAsync(output, .5, 40, 30)).Blue > 220);
        var (_, samples) = await SamplesAsync(output);
        Assert.True(Energy(samples, .3, .42) < 200);
        Assert.InRange(Tone(samples, .52, .65), 870, 890);
    }

    [FfmpegFact]
    public async Task DeclaredFirstFrameNormalizationRemovesSourceAudioStartOffsetWithoutLosingContent()
    {
        var input = await SourceAsync("red", 80, 60, 10, .4, audioDuration: .4, audioOffset: .2);
        var sourceJson = await Runner().RunAsync("ffprobe",
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time", "-of", "json", input], default);
        using (var document = JsonDocument.Parse(sourceJson))
            Assert.InRange(double.Parse(document.RootElement.GetProperty("streams")[0].GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture), .19, .21);
        var output = Path.Combine(_root, "offset-normalized.mp4");
        var result = await new VideoProcessor().ConcatenateAsync([input], output, new() { FrameRate = 10 });
        Assert.InRange(result.Duration!.Value.TotalSeconds, .59, .65);
        var (_, samples) = await SamplesAsync(output);
        Assert.True(Energy(samples, .03, .15) > 1_000);
        Assert.InRange(Tone(samples, .03, .15), 430, 450);
        Assert.True(Energy(samples, .48, .56) < 200);
    }

    [FfmpegFact]
    public async Task NonsquareSarUsesDisplayAspectWithoutAnOversizedIntermediateRaster()
    {
        var input = await SourceAsync("red", 80, 60, 10, .4, sar: "2/1");
        var output = Path.Combine(_root, "sar.mp4");
        var result = await new VideoProcessor().ConcatenateAsync([input], output,
            new() { Canvas = new(120, 120), MaximumDimension = 120, MaximumPixels = 14_400 });
        Assert.Equal((120, 120), (result.Width, result.Height));
        Assert.True((await FramePixelAsync(output, .1, 60, 20)).Red < 20);
        Assert.True((await FramePixelAsync(output, .1, 60, 40)).Red > 220);
        var json = await Runner().RunAsync("ffprobe",
            ["-v", "error", "-select_streams", "V:0", "-show_entries", "stream=sample_aspect_ratio", "-of", "json", output], default);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("1:1", document.RootElement.GetProperty("streams")[0].GetProperty("sample_aspect_ratio").GetString());
    }

    [FfmpegTheory]
    [InlineData(90, 40, 80)]
    [InlineData(180, 80, 40)]
    [InlineData(270, 40, 80)]
    public async Task RotationMetadataIsAppliedAndRemovedFromTheNormalizedOutput(int rotation, int width, int height)
    {
        var original = await SourceAsync("red", 80, 40, 10, .4, extension: ".mp4", twoColors: true);
        var rotated = Path.Combine(_root, "rotated.mp4");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-display_rotation", rotation.ToString(CultureInfo.InvariantCulture),
            "-i", original, "-c", "copy", rotated], default);
        Assert.Equal(rotation, (await new VideoProcessor().ProbeAsync(rotated)).RotationDegrees);
        var output = Path.Combine(_root, "rotation-normalized.mp4");
        var result = await new VideoProcessor().ConcatenateAsync([rotated], output);
        Assert.Equal((width, height), (result.Width, result.Height));
        Assert.Equal(0, result.RotationDegrees);
        foreach (var (x, y) in new[] { (width / 4, height / 4), (width * 3 / 4, height * 3 / 4) })
        {
            var reference = await FramePixelAsync(rotated, .1, x, y);
            var actual = await FramePixelAsync(output, .1, x, y);
            Assert.Equal(reference.Red > reference.Blue, actual.Red > actual.Blue);
        }
    }

    [FfmpegFact]
    public async Task VariableInputFrameTimesBecomeConstantOutputFrameTimes()
    {
        var input = await SourceAsync("red", 80, 60, 20, .8, variableRate: true);
        var initial = await FrameTimesAsync(input);
        Assert.True(initial.Zip(initial.Skip(1), (first, second) => Math.Round(second - first, 3)).Distinct().Count() > 1);
        var output = Path.Combine(_root, "cfr.mp4");
        await new VideoProcessor().ConcatenateAsync([input], output, new() { FrameRate = 10 });
        var frames = await FrameTimesAsync(output);
        Assert.True(frames.Length >= 8);
        Assert.All(frames.Zip(frames.Skip(1), (first, second) => second - first), difference => Assert.InRange(difference, .099, .101));
    }

    [FfmpegFact]
    public async Task MultipleSourceAudioTracksUseOnlyTheDeclaredFirstTrack()
    {
        var input = await SourceAsync("red", 80, 60, 10, .4, audioDuration: .4, secondAudio: true);
        var output = Path.Combine(_root, "first-audio.mp4");
        await new VideoProcessor().ConcatenateAsync([input], output);
        var (_, samples) = await SamplesAsync(output);
        Assert.InRange(Tone(samples, .08, .3), 430, 450);
        var json = await Runner().RunAsync("ffprobe", ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "json", output], default);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("streams").GetArrayLength());
    }

    [FfmpegFact]
    public async Task UnknownDurationAndOverBudgetInputAreRejectedBeforePublishing()
    {
        var input = await SourceAsync("red", 80, 60, 10, .4);
        var raw = Path.Combine(_root, "unknown.h264");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-i", input, "-an", "-c:v", "libx264", "-f", "h264", raw], default);
        var output = Path.Combine(_root, "unsupported.mp4");
        await Assert.ThrowsAsync<NotSupportedException>(() => new VideoProcessor().ConcatenateAsync([raw], output, new() { FrameRate = 10 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VideoProcessor().ConcatenateAsync([input], output,
            new() { Canvas = new(10, 10), MaximumPixels = 80 * 60 - 1 }));
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task FailedEncoderDeletesOnlyItsOwnedStagingFile()
    {
        var input = await SourceAsync("red", 80, 60, 10, .2);
        var output = Path.Combine(_root, "failed.mp4");
        var keep = Path.Combine(_root, "keep.txt");
        await File.WriteAllTextAsync(keep, "keep");
        await Assert.ThrowsAsync<MediaProcessException>(() => new VideoProcessor(new() { FfmpegPath = "ffprobe" }).ConcatenateAsync([input], output));
        Assert.False(File.Exists(output));
        Assert.Equal("keep", await File.ReadAllTextAsync(keep));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task EmptyExcessiveOrInvalidOptionsFailBeforeProcessesAndDirectoryCreation()
    {
        var processor = new VideoProcessor(new() { FfprobePath = "not-installed" });
        var output = Path.Combine(_root, "output.mp4");
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ConcatenateAsync([], output));
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ConcatenateAsync(Enumerable.Repeat("missing.mkv", 5).ToArray(), output,
            new() { MaximumInputs = 4 }));
        VideoConcatenationOptions[] invalid =
        [new() { MaximumInputs = 0 }, new() { FrameRate = 0 }, new() { FrameRate = double.NaN },
            new() { FrameRate = double.PositiveInfinity }, new() { FrameRate = 241 }, new() { ConstantRateFactor = 52 }];
        foreach (var options in invalid)
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ConcatenateAsync(["missing.mkv"], output, options));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ConcatenateAsync(["missing.mkv"], output,
            new() { Canvas = new(99, 99), MaximumPixels = 99 * 99 }));
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ConcatenateAsync(["missing.mkv"], Path.Combine(_root, "output.gif")));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task ExistingTargetsAndEveryInputOutputAliasAreProtected()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "keep.mp4");
        await File.WriteAllTextAsync(output, "keep");
        var processor = new VideoProcessor(new() { FfprobePath = "not-installed" });
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ConcatenateAsync(["missing.mkv", output], output));
        await Assert.ThrowsAsync<IOException>(() => processor.ConcatenateAsync(["missing.mkv"], output));
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
        var directory = Path.Combine(_root, "directory.mp4");
        Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<IOException>(() => processor.ConcatenateAsync(["missing.mkv"], directory));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task PreCancellationCreatesNoOutputDirectory()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoProcessor().ConcatenateAsync(["missing.mkv"],
            Path.Combine(_root, "cancel.mp4"), cancellationToken: new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    private async Task<string> SourceAsync(string color, int width, int height, int rate, double videoDuration,
        double? audioDuration = null, int frequency = 440, string extension = ".mkv", string sar = "1/1",
        double audioOffset = 0, bool variableRate = false, bool secondAudio = false, bool twoColors = false)
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, Guid.NewGuid().ToString("N") + extension);
        var arguments = new List<string> { "-v", "error", "-nostdin", "-f", "lavfi", "-i",
            FormattableString.Invariant($"color=c={color}:s={width}x{height}:r={rate}:d={videoDuration}") };
        if (audioDuration is { } duration)
        {
            if (audioOffset != 0) arguments.AddRange(["-itsoffset", audioOffset.ToString("R", CultureInfo.InvariantCulture)]);
            arguments.AddRange(["-f", "lavfi", "-i", FormattableString.Invariant($"sine=frequency={frequency}:sample_rate=48000:duration={duration}")]);
            if (secondAudio)
                arguments.AddRange(["-f", "lavfi", "-i", FormattableString.Invariant($"sine=frequency=880:sample_rate=48000:duration={duration}")]);
        }
        arguments.AddRange(["-map", "0:v:0"]);
        if (audioDuration.HasValue) arguments.AddRange(["-map", "1:a:0"]);
        if (secondAudio) arguments.AddRange(["-map", "2:a:0"]);
        var filter = (variableRate ? "select='eq(mod(n,3),0)+eq(mod(n,5),0)'," : "") +
            (twoColors ? "drawbox=x=0:y=0:w=iw/2:h=ih:color=blue:t=fill," : "") + "setsar=" + sar;
        arguments.AddRange(["-vf", filter, "-fps_mode", "vfr", "-c:v", extension == ".mp4" ? "libx264" : "ffv1", "-pix_fmt", "yuv420p"]);
        if (audioDuration.HasValue) arguments.AddRange(["-c:a", extension == ".mp4" ? "aac" : "pcm_s16le"]);
        arguments.Add(output);
        await Runner().RunAsync("ffmpeg", arguments, default);
        return output;
    }

    private async Task<SKColor> FramePixelAsync(string video, double seconds, int x, int y)
    {
        var output = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".png");
        await new VideoProcessor().CaptureFrameAsync(video, output, TimeSpan.FromSeconds(seconds));
        using var bitmap = SKBitmap.Decode(output);
        return bitmap.GetPixel(x, y);
    }

    private async Task<(AudioExtractionResult Audio, short[] Samples)> SamplesAsync(string video)
    {
        var wav = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".wav");
        var audio = await new VideoProcessor().ExtractAudioAsync(video, wav);
        var pcm = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".pcm");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-i", wav, "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", pcm], default);
        var bytes = await File.ReadAllBytesAsync(pcm);
        return (audio, Enumerable.Range(0, bytes.Length / 2).Select(index => BitConverter.ToInt16(bytes, index * 2)).ToArray());
    }

    private async Task<double[]> FrameTimesAsync(string video)
    {
        var json = await Runner().RunAsync("ffprobe", ["-v", "error", "-select_streams", "V:0", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", video], default);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("frames").EnumerateArray()
            .Select(frame => double.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
    }

    private static double Energy(short[] samples, double start, double end)
        => Math.Sqrt(samples.Skip((int)(start * 48_000)).Take((int)((end - start) * 48_000)).Average(value => (double)value * value));

    private static double Tone(short[] samples, double start, double end)
    {
        var slice = samples.Skip((int)(start * 48_000)).Take((int)((end - start) * 48_000)).ToArray();
        var threshold = slice.Max(value => Math.Abs((int)value)) / 4;
        var count = 0; var first = 0; var last = 0; var armed = false;
        for (var index = 0; index < slice.Length; index++)
        {
            if (slice[index] <= -threshold) armed = true;
            if (!armed || slice[index] < threshold) continue;
            if (count++ == 0) first = index;
            last = index; armed = false;
        }
        Assert.True(count > 2, "Expected a non-silent tone.");
        return (count - 1) * 48_000d / (last - first);
    }

    private static MediaProcessRunner Runner() => new(TimeSpan.FromSeconds(45));
    private void AssertNoTemporaryFiles() => Assert.Empty(Directory.GetFiles(_root, ".video-*"));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoConcatenationTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
