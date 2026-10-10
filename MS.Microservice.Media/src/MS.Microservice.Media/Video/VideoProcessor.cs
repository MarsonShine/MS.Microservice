using System.Globalization;
using System.Text.Json;

namespace MS.Microservice.Media.Video;

/// <summary>Inspects and processes local media using installed FFmpeg tools.</summary>
public sealed partial class VideoProcessor
{
    // Complex graphs may propagate the input display matrix after autorotation; encoded pixels are already oriented.
    private const string ClearAppliedRotationFilter = "sidedata=mode=delete:type=DISPLAYMATRIX";
    private readonly FfmpegOptions _options;
    private readonly MediaProcessRunner _runner;

    public VideoProcessor(FfmpegOptions? options = null)
    {
        _options = options ?? new FfmpegOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.FfmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.FfprobePath);
        if (_options.Timeout <= TimeSpan.Zero && _options.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), "The process timeout must be positive or infinite.");
        _runner = new MediaProcessRunner(_options.Timeout);
    }

    public async Task<VideoInfo> ProbeAsync(string inputFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);
        cancellationToken.ThrowIfCancellationRequested();
        var input = Path.GetFullPath(inputFile);
        if (!File.Exists(input))
            throw new FileNotFoundException("The media input file does not exist.", input);
        var json = await _runner.RunAsync(_options.FfprobePath,
        [
            "-v", "error", "-show_entries",
            "stream=codec_type,codec_name,width,height,avg_frame_rate,duration:stream_disposition=attached_pic:stream_tags=rotate:stream_side_data=rotation:format=duration",
            "-of", "json", input
        ], cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return ParseMetadata(document.RootElement);
    }

    internal static VideoInfo ParseMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The media contains no stream metadata.");
        var video = streams.EnumerateArray().FirstOrDefault(stream =>
            Text(stream, "codec_type") == "video" && !IsAttachedPicture(stream));
        if (video.ValueKind == JsonValueKind.Undefined)
            throw new NotSupportedException("The media contains no video track.");
        if (!video.TryGetProperty("width", out var widthValue) || !widthValue.TryGetInt32(out var width) || width < 1
            || !video.TryGetProperty("height", out var heightValue) || !heightValue.TryGetInt32(out var height) || height < 1)
            throw new InvalidDataException("The video dimensions are invalid.");

        var rotation = 0;
        var hasRotation = false;
        if (video.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var side in sideData.EnumerateArray())
            {
                if (side.TryGetProperty("rotation", out var value) && value.TryGetInt32(out rotation))
                {
                    hasRotation = true;
                    break;
                }
            }
        }
        if (!hasRotation && video.TryGetProperty("tags", out var tags))
            int.TryParse(Text(tags, "rotate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out rotation);
        rotation = (rotation % 360 + 360) % 360;
        if (rotation is 90 or 270)
            (width, height) = (height, width);
        var seconds = root.TryGetProperty("format", out var format) ? Number(Text(format, "duration")) : null;
        seconds ??= Number(Text(video, "duration"));
        if (seconds is < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
            throw new InvalidDataException("The video duration is outside the supported range.");
        var duration = seconds is { } finite ? TimeSpan.FromSeconds(finite) : (TimeSpan?)null;
        var fpsParts = Text(video, "avg_frame_rate")?.Split('/');
        double? fps = null;
        if (fpsParts is { Length: 2 } && Number(fpsParts[0]) is { } numerator
            && Number(fpsParts[1]) is { } denominator && denominator > 0)
        {
            var rate = numerator / denominator;
            if (double.IsFinite(rate) && rate > 0) fps = rate;
        }
        return new(width, height, Text(video, "codec_name"), duration, fps,
            streams.EnumerateArray().Any(stream => Text(stream, "codec_type") == "audio"), rotation);
    }

    private static bool IsAttachedPicture(JsonElement stream)
        => stream.TryGetProperty("disposition", out var disposition)
            && disposition.TryGetProperty("attached_pic", out var value) && value.GetInt32() != 0;

    private static string? Text(JsonElement value, string name)
        => value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() : null;

    private static double? Number(string? text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
}
