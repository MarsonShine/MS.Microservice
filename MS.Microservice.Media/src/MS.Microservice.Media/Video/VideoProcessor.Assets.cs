using System.Globalization;
using System.Text.Json;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed partial class VideoProcessor
{
    /// <summary>Captures the first frame at or after a timestamp and publishes a new image file.</summary>
    public async Task<ImageProcessingResult> CaptureFrameAsync(string inputFile, string outputFile,
        TimeSpan timestamp = default, ImageProcessingOptions? imageOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (input, output, extension) = ValidateAssetPaths(inputFile, outputFile);
        var format = extension switch
        {
            ".png" => ImageOutputFormat.Png, ".jpg" or ".jpeg" => ImageOutputFormat.Jpeg,
            ".webp" => ImageOutputFormat.Webp,
            _ => throw new NotSupportedException("Frame capture outputs PNG, JPEG or WebP.")
        };
        imageOptions ??= new ImageProcessingOptions { Format = format };
        if (timestamp < TimeSpan.Zero || imageOptions.Format != format)
            throw new ArgumentException("Choose a nonnegative timestamp and image format matching the output extension.");
        ImageCodec.ValidateLimits(imageOptions.Limits);
        if (imageOptions.Resize is { } resize)
            ImageCodec.CheckDimensions(resize.Width, resize.Height, imageOptions.Limits);
        var info = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
        ImageCodec.CheckDimensions(info.Width, info.Height, imageOptions.Limits);
        if (info.Duration is { } length && timestamp >= length)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "The timestamp must precede the video end.");

        return await PublishFileAsync(output, extension, async temporary =>
        {
            await _runner.RunAsync(_options.FfmpegPath,
            [
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", Seconds(timestamp), "-i", input,
                "-map", "0:V:0", "-frames:v", "1", "-an", "-sn", "-dn", "-c:v", "png",
                "-fps_mode", "passthrough", "-f", "image2", "-update", "1", temporary
            ], cancellationToken).ConfigureAwait(false);
            if (new FileInfo(temporary).Length == 0)
                throw new InvalidDataException("No video frame is available at or after the requested timestamp.");

            // One owned staging file: decode lossless PNG, then replace it with the requested encoded image.
            using var encoded = new MemoryStream();
            ImageProcessingResult result;
            await using (var captured = File.OpenRead(temporary))
                result = await new ImageProcessor().ProcessAsync(captured, encoded, imageOptions, cancellationToken).ConfigureAwait(false);
            encoded.Position = 0;
            await using (var destination = new FileStream(temporary, FileMode.Truncate, FileAccess.Write, FileShare.None))
                await encoded.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Extracts one video audio track, optionally trimming it, into a new audio file.</summary>
    public async Task<AudioExtractionResult> ExtractAudioAsync(string inputFile, string outputFile,
        AudioExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (input, output, extension) = ValidateAssetPaths(inputFile, outputFile);
        var codec = extension switch
        {
            ".wav" => "pcm_s16le", ".mp3" => "libmp3lame", ".m4a" => "aac", ".flac" => "flac",
            _ => throw new NotSupportedException("Audio extraction outputs WAV, MP3, M4A or FLAC.")
        };
        options ??= new AudioExtractionOptions();
        if (options.AudioTrackIndex < 0 || options.Start < TimeSpan.Zero || options.Duration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentException("Choose a nonnegative audio track/start and positive optional duration.", nameof(options));
        var video = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
        if (!video.HasAudio) throw new NotSupportedException("The video has no audio track.");
        var selected = await ProbeAudioAssetAsync(input, options.AudioTrackIndex, false, cancellationToken).ConfigureAwait(false);
        if (selected.Duration is { } length && options.Start >= length)
            throw new ArgumentException("The audio start must precede the selected track end.", nameof(options));
        if (extension == ".mp3" && selected.Channels > 2)
            throw new NotSupportedException("MP3 extraction supports mono/stereo input; choose WAV, M4A or FLAC for multichannel audio.");

        return await PublishFileAsync(output, extension, async temporary =>
        {
            var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input };
            if (options.Start > TimeSpan.Zero) arguments.AddRange(["-ss", Seconds(options.Start)]);
            if (options.Duration is { } time) arguments.AddRange(["-t", Seconds(time)]);
            arguments.AddRange(["-map", $"0:a:{options.AudioTrackIndex.ToString(CultureInfo.InvariantCulture)}",
                "-vn", "-sn", "-dn", "-map_metadata", "-1", "-c:a", codec,
                "-ar", selected.SampleRate.ToString(CultureInfo.InvariantCulture),
                "-ac", selected.Channels.ToString(CultureInfo.InvariantCulture)]);
            if (extension is ".mp3" or ".m4a") arguments.AddRange(["-b:a", "192k"]);
            arguments.Add(temporary);
            await _runner.RunAsync(_options.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
            var result = await ProbeAudioAssetAsync(temporary, 0, true, cancellationToken).ConfigureAwait(false);
            if (result.SampleRate != selected.SampleRate || result.Channels != selected.Channels)
                throw new InvalidDataException("The encoder changed the selected track's sample rate or channel count.");
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static (string Input, string Output, string Extension) ValidateAssetPaths(string inputFile, string outputFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);
        var input = Path.GetFullPath(inputFile);
        var output = Path.GetFullPath(outputFile);
        if (input.Equals(output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Input and output must be different files.", nameof(outputFile));
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The output already exists.");
        return (input, output, Path.GetExtension(output).ToLowerInvariant());
    }

    private async Task<AudioExtractionResult> ProbeAudioAssetAsync(string input, int track, bool verifySamples,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-v", "error", "-select_streams", "a:" + track.ToString(CultureInfo.InvariantCulture) };
        if (verifySamples) arguments.Add("-count_frames");
        arguments.AddRange(["-show_entries", "stream=codec_name,sample_rate,channels,duration,nb_read_frames:format=duration", "-of", "json", input]);
        var json = await _runner.RunAsync(_options.FfprobePath, arguments, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var audio = root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array
            ? streams.EnumerateArray().FirstOrDefault() : default;
        if (audio.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentOutOfRangeException(nameof(track), "The requested audio track does not exist.");
        if (!int.TryParse(Text(audio, "sample_rate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) || rate < 1 ||
            !audio.TryGetProperty("channels", out var channelValue) || !channelValue.TryGetInt32(out var channels) || channels < 1)
            throw new InvalidDataException("The audio sample rate or channel count is invalid.");
        if (verifySamples && (!long.TryParse(Text(audio, "nb_read_frames"), out var frames) || frames < 1))
            throw new InvalidDataException("The extracted audio contains no decodable sample frames.");
        var seconds = Number(Text(audio, "duration"));
        if (seconds is null && root.TryGetProperty("format", out var format)) seconds = Number(Text(format, "duration"));
        if (seconds is < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
            throw new InvalidDataException("The audio duration is outside the supported range.");
        return new(Text(audio, "codec_name"), rate, channels, seconds is { } finite ? TimeSpan.FromSeconds(finite) : null);
    }
}
