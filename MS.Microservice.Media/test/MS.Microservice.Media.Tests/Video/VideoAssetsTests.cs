using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Video;

public sealed class VideoAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoAssetsTests", Guid.NewGuid().ToString("N"));

    [FfmpegTheory]
    [InlineData(".png", ImageOutputFormat.Png)]
    [InlineData(".jpg", ImageOutputFormat.Jpeg)]
    [InlineData(".jpeg", ImageOutputFormat.Jpeg)]
    [InlineData(".webp", ImageOutputFormat.Webp)]
    public async Task CaptureProducesRealEncodedFrameAtRequestedTime(string extension, ImageOutputFormat format)
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "封面 with 'quotes;[x]" + extension);
        var result = await new VideoProcessor().CaptureFrameAsync(input, output, TimeSpan.FromSeconds(.5));
        Assert.Equal(new ImageProcessingResult(80, 60, format), result);
        using var bitmap = SKBitmap.Decode(output);
        var pixel = bitmap.GetPixel(40, 30);
        Assert.True(pixel.Blue > 230 && pixel.Red < 20 && pixel.Green < 20);
        Assert.True(File.Exists(input));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task CaptureAtZeroAndLastFrameUsesRealPixelContent()
    {
        var input = await SourceAsync();
        var processor = new VideoProcessor();
        var first = Path.Combine(_root, "first.png");
        var last = Path.Combine(_root, "last.png");
        await processor.CaptureFrameAsync(input, first);
        await processor.CaptureFrameAsync(input, last, TimeSpan.FromSeconds(.7));
        using var firstBitmap = SKBitmap.Decode(first);
        using var lastBitmap = SKBitmap.Decode(last);
        Assert.True(firstBitmap.GetPixel(40, 30).Red > 230);
        Assert.True(lastBitmap.GetPixel(40, 30).Blue > 230);
    }

    [FfmpegFact]
    public async Task CaptureReusesImageTransformsAndRejectsRasterOrEncodedInputBudgetOverruns()
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "transformed.png");
        var processor = new VideoProcessor();
        var result = await processor.CaptureFrameAsync(input, output, TimeSpan.FromSeconds(.5),
            new() { Crop = new(5, 5, 40, 30), RotationDegrees = 90, Resize = new(40, 40) });
        Assert.Equal((30, 40), (result.Width, result.Height));
        var limited = Path.Combine(_root, "limited.png");
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.CaptureFrameAsync(input, limited,
            imageOptions: new() { Limits = new() { MaximumPixels = 80 * 60 - 1 } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.CaptureFrameAsync(input, limited,
            imageOptions: new() { Limits = new() { MaximumEncodedBytes = 1 } }));
        Assert.False(File.Exists(limited));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task CaptureRejectsVideoEndAndNoRemainingFrameWithoutPublishing()
    {
        var input = await SourceAsync();
        var output = Path.Combine(_root, "past-end.png");
        var processor = new VideoProcessor();
        var info = await processor.ProbeAsync(input);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.CaptureFrameAsync(input, output, info.Duration!.Value));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.CaptureFrameAsync(input, output, TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.CaptureFrameAsync(input, output, TimeSpan.FromSeconds(.79)));
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    [FfmpegTheory]
    [InlineData(".wav", "pcm_s16le")]
    [InlineData(".mp3", "mp3")]
    [InlineData(".m4a", "aac")]
    [InlineData(".flac", "flac")]
    public async Task ExtractProducesDecodableAudioWithCorrectCodecSamplesAndDuration(string extension, string codec)
    {
        var input = await SourceAsync(withAudio: true);
        var output = Path.Combine(_root, "提取 with spaces" + extension);
        var result = await new VideoProcessor().ExtractAudioAsync(input, output);
        Assert.Equal(codec, result.Codec);
        Assert.Equal(48_000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.InRange(result.Duration!.Value.TotalSeconds, .75, .9);
        var samples = await DecodeSamplesAsync(output);
        Assert.InRange(samples.Length, 38_000, 41_000);
        Assert.InRange(Frequency(samples), 435, 445);
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task ExtractSelectsTheRequestedAudioTrackAndTrimsDecodedSamples()
    {
        var input = await SourceAsync(withAudio: true, secondTrack: true);
        var output = Path.Combine(_root, "second.wav");
        var result = await new VideoProcessor().ExtractAudioAsync(input, output,
            new() { AudioTrackIndex = 1, Start = TimeSpan.FromSeconds(.2), Duration = TimeSpan.FromSeconds(.3) });
        var samples = await DecodeSamplesAsync(output);
        Assert.InRange(samples.Length, 14_390, 14_410);
        Assert.InRange(Frequency(samples), 870, 890);
        Assert.InRange(result.Duration!.Value.TotalSeconds, .299, .301);
    }

    [FfmpegFact]
    public async Task ExtractDurationPastEndClampsAndInvalidTrackOrStartDoesNotPublish()
    {
        var input = await SourceAsync(withAudio: true);
        var processor = new VideoProcessor();
        var output = Path.Combine(_root, "clamped.wav");
        var result = await processor.ExtractAudioAsync(input, output,
            new() { Start = TimeSpan.FromSeconds(.2), Duration = TimeSpan.FromSeconds(10) });
        Assert.InRange(result.Duration!.Value.TotalSeconds, .599, .601);
        var invalid = Path.Combine(_root, "invalid.wav");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ExtractAudioAsync(input, invalid,
            new() { AudioTrackIndex = 99 }));
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ExtractAudioAsync(input, invalid,
            new() { Start = TimeSpan.FromSeconds(.8) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ExtractAudioAsync(input, invalid,
            new() { Duration = TimeSpan.FromTicks(1) }));
        Assert.False(File.Exists(invalid));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task SilentVideoAndAudioOnlyInputProduceClearErrors()
    {
        var silent = await SourceAsync();
        var processor = new VideoProcessor();
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractAudioAsync(silent, Path.Combine(_root, "silent.wav")));
        var audio = Path.Combine(_root, "audio-only.wav");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "sine=duration=0.1", audio], default);
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.CaptureFrameAsync(audio, Path.Combine(_root, "nonvideo.png")));
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractAudioAsync(audio, Path.Combine(_root, "nonvideo.wav")));
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task Mp3RejectsMultichannelInputBeforeImplicitDownmixWhileWavPreservesIt()
    {
        var input = await SourceAsync(withAudio: true, surround: true);
        var processor = new VideoProcessor();
        var output = Path.Combine(_root, "surround.mp3");
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractAudioAsync(input, output));
        Assert.False(File.Exists(output));
        var wav = await processor.ExtractAudioAsync(input, Path.Combine(_root, "surround.wav"));
        Assert.Equal(6, wav.Channels);
        AssertNoTemporaryFiles();
    }

    [FfmpegFact]
    public async Task UnsupportedEncoderSampleRateFailsAtomicallyWhilePcmPreservesIt()
    {
        var input = await SourceAsync(withAudio: true, sampleRate: 96_000);
        var processor = new VideoProcessor();
        var wav = await processor.ExtractAudioAsync(input, Path.Combine(_root, "high-rate.wav"));
        Assert.Equal(96_000, wav.SampleRate);
        Assert.Equal(1, wav.Channels);
        var mp3 = Path.Combine(_root, "high-rate.mp3");
        await Assert.ThrowsAsync<MediaProcessException>(() => processor.ExtractAudioAsync(input, mp3));
        Assert.False(File.Exists(mp3));
        AssertNoTemporaryFiles();
    }

    [FfmpegTheory]
    [InlineData(44_100)]
    [InlineData(32_000)]
    public async Task Mp3PreservesSupportedNonDefaultSampleRates(int sampleRate)
    {
        var input = await SourceAsync(withAudio: true, sampleRate: sampleRate);
        var output = Path.Combine(_root, "different-rate.mp3");
        var result = await new VideoProcessor().ExtractAudioAsync(input, output);
        Assert.Equal(sampleRate, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.InRange(Frequency(await DecodeSamplesAsync(output)), 435, 445);
    }

    [FfmpegTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncoderFailureDeletesOnlyOwnedTemporaryFile(bool audio)
    {
        var input = await SourceAsync(withAudio: true);
        var output = Path.Combine(_root, audio ? "failed.wav" : "failed.png");
        var keep = Path.Combine(_root, "keep.txt");
        await File.WriteAllTextAsync(keep, "keep");
        var processor = new VideoProcessor(new() { FfmpegPath = "ffprobe" });
        if (audio)
            await Assert.ThrowsAsync<MediaProcessException>(() => processor.ExtractAudioAsync(input, output));
        else
            await Assert.ThrowsAsync<MediaProcessException>(() => processor.CaptureFrameAsync(input, output));
        Assert.False(File.Exists(output));
        Assert.Equal("keep", await File.ReadAllTextAsync(keep));
        AssertNoTemporaryFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingFilesDirectoriesAndSamePathAreNeverOverwritten(bool audio)
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, audio ? "keep.wav" : "keep.png");
        await File.WriteAllTextAsync(output, "keep");
        var processor = new VideoProcessor(new() { FfprobePath = "not-installed" });
        async Task Run(string input, string target)
        {
            if (audio) await processor.ExtractAudioAsync(input, target);
            else await processor.CaptureFrameAsync(input, target);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => Run(output, output));
        await Assert.ThrowsAsync<IOException>(() => Run("missing.mkv", output));
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
        var directory = Path.Combine(_root, audio ? "directory.wav" : "directory.png");
        Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<IOException>(() => Run("missing.mkv", directory));
        Assert.True(Directory.Exists(directory));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task InvalidOptionsAndFormatsFailBeforeProcessesOrDirectoriesAreCreated()
    {
        var processor = new VideoProcessor(new() { FfprobePath = "not-installed" });
        var image = Path.Combine(_root, "out.png");
        var audio = Path.Combine(_root, "out.wav");
        await Assert.ThrowsAsync<ArgumentException>(() => processor.CaptureFrameAsync("input.mkv", image, TimeSpan.FromTicks(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => processor.CaptureFrameAsync("input.mkv", image,
            imageOptions: new() { Format = ImageOutputFormat.Jpeg }));
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.CaptureFrameAsync("input.mkv", Path.Combine(_root, "out.bmp")));
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ExtractAudioAsync("input.mkv", Path.Combine(_root, "out.aac")));
        AudioExtractionOptions[] invalid =
        [new() { AudioTrackIndex = -1 }, new() { Start = TimeSpan.FromTicks(-1) }, new() { Duration = TimeSpan.Zero }];
        foreach (var options in invalid)
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ExtractAudioAsync("input.mkv", audio, options));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancellationStartsNoProcessAndCreatesNoFiles(bool audio)
    {
        var processor = new VideoProcessor(new() { FfprobePath = "not-installed" });
        if (audio)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ExtractAudioAsync("input.mkv",
                Path.Combine(_root, "cancel.wav"), cancellationToken: new CancellationToken(true)));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.CaptureFrameAsync("input.mkv",
                Path.Combine(_root, "cancel.png"), cancellationToken: new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    private async Task<string> SourceAsync(bool withAudio = false, bool secondTrack = false, bool surround = false, int sampleRate = 48_000)
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "输入 with 'quotes;[x].mkv");
        var arguments = new List<string>
        {
            "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=red:s=80x60:r=10:d=0.4",
            "-f", "lavfi", "-i", "color=c=blue:s=80x60:r=10:d=0.4"
        };
        if (withAudio)
            arguments.AddRange(["-f", "lavfi", "-i", surround
                ? FormattableString.Invariant($"anullsrc=r={sampleRate}:cl=5.1:d=0.8")
                : FormattableString.Invariant($"sine=frequency=440:sample_rate={sampleRate}:duration=0.8")]);
        if (secondTrack)
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=0.8"]);
        arguments.AddRange(["-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]"]);
        if (withAudio) arguments.AddRange(["-map", "2:a"]);
        if (secondTrack) arguments.AddRange(["-map", "3:a"]);
        arguments.AddRange(["-c:v", "ffv1", "-pix_fmt", "yuv420p"]);
        if (withAudio) arguments.AddRange(["-c:a", "pcm_s16le"]);
        arguments.AddRange(["-t", "0.8", input]);
        await Runner().RunAsync("ffmpeg", arguments, default);
        return input;
    }

    private async Task<short[]> DecodeSamplesAsync(string input)
    {
        var output = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".pcm");
        await Runner().RunAsync("ffmpeg", ["-v", "error", "-nostdin", "-i", input, "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", output], default);
        var bytes = await File.ReadAllBytesAsync(output);
        return Enumerable.Range(0, bytes.Length / 2).Select(index => BitConverter.ToInt16(bytes, index * 2)).ToArray();
    }

    private static double Frequency(short[] samples)
    {
        // Ignore lossy near-zero ringing and encoder padding; measure periods between strong crossings.
        var threshold = samples.Max(value => Math.Abs((int)value)) / 4;
        var crossings = 0;
        var first = 0;
        var last = 0;
        var armed = false;
        for (var index = 0; index < samples.Length; index++)
        {
            if (samples[index] <= -threshold) armed = true;
            if (!armed || samples[index] < threshold) continue;
            if (crossings++ == 0) first = index;
            last = index;
            armed = false;
        }
        Assert.True(crossings > 2, "Expected a measurable non-silent tone.");
        return (crossings - 1) * 48_000d / (last - first);
    }

    private static MediaProcessRunner Runner() => new(TimeSpan.FromSeconds(30));
    private void AssertNoTemporaryFiles() => Assert.Empty(Directory.GetFiles(_root, ".video-*"));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.VideoAssetsTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
