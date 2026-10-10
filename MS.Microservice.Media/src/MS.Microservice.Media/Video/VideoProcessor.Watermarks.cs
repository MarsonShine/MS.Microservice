using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed partial class VideoProcessor
{
    public Task<VideoInfo> AddTextWatermarkAsync(string inputFile, string outputFile,
        TextWatermark text, VideoWatermarkOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new();
        return WatermarkAsync(inputFile, outputFile, options, (info, overlay) =>
            new ImageWatermarker().RenderTextOverlayAsync(info.Width, info.Height, overlay, text,
                options.Placement, cancellationToken), cancellationToken);
    }

    public Task<VideoInfo> AddLogoWatermarkAsync(string inputFile, string outputFile,
        Stream logo, ImageSize? logoSize = null, VideoWatermarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logo);
        options ??= new();
        return WatermarkAsync(inputFile, outputFile, options, (info, overlay) =>
            new ImageWatermarker().RenderLogoOverlayAsync(info.Width, info.Height, logo, overlay, logoSize,
                options.Placement, cancellationToken), cancellationToken);
    }

    private async Task<VideoInfo> WatermarkAsync(string inputFile, string outputFile, VideoWatermarkOptions options,
        Func<VideoInfo, Stream, Task> render, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (input, output, extension) = ValidateAssetPaths(inputFile, outputFile);
        if (extension is not (".mp4" or ".mkv" or ".webm"))
            throw new NotSupportedException("Video watermarking outputs MP4, MKV or WebM.");
        ArgumentNullException.ThrowIfNull(options.Placement);
        if (options.Start < TimeSpan.Zero || options.End is { } end && end <= options.Start
            || options.ConstantRateFactor is < 0 or > 51 || !Enum.IsDefined(options.Speed))
            throw new ArgumentException("Choose a nonnegative start, later optional end and valid encoding quality/speed.", nameof(options));
        ImageCodec.ValidateLimits(options.Placement.Limits);
        var info = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
        ImageCodec.CheckDimensions(info.Width, info.Height, options.Placement.Limits);
        ImageCodec.CheckDimensions(checked(info.Width + info.Width % 2), checked(info.Height + info.Height % 2), options.Placement.Limits);
        if (info.Duration is { } length && options.Start >= length)
            throw new ArgumentException("The watermark start must precede the video end.", nameof(options));

        return await PublishFileAsync(output, extension, async temporary =>
        {
            var overlay = temporary + ".png";
            Exception? processingError = null;
            var ownsOverlay = false;
            try
            {
                await using (var stream = new FileStream(overlay, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsOverlay = true;
                    await render(info, stream).ConfigureAwait(false);
                }
                var enable = "gte(t," + Seconds(options.Start) + ")";
                if (options.End is { } last) enable += "*lt(t," + Seconds(last) + ")";
                var arguments = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input,
                    "-loop", "1", "-framerate", "1", "-i", overlay,
                    "-filter_complex", "[0:V:0][1:v:0]overlay=0:0:shortest=1:format=auto:enable='" + enable
                        + "',pad=ceil(iw/2)*2:ceil(ih/2)*2," + ClearAppliedRotationFilter + "[v]",
                    "-map", "[v]", "-map", "0:a?", "-map_metadata", "0",
                    "-metadata:s:v:0", "rotate=0", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough"
                };
                AddVideoEncodingArguments(arguments, extension, options.ConstantRateFactor, options.Speed);
                arguments.Add(temporary);
                await _runner.RunAsync(_options.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
                var result = await ProbeAsync(temporary, cancellationToken).ConfigureAwait(false);
                if (result.Width != info.Width + info.Width % 2 || result.Height != info.Height + info.Height % 2
                    || result.HasAudio != info.HasAudio)
                    throw new InvalidDataException("Watermark output changed the expected raster or presence of audio.");
                return result;
            }
            catch (Exception error) { processingError = error; throw; }
            finally
            {
                try { if (ownsOverlay) File.Delete(overlay); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    if (processingError is null) throw;
                    throw new AggregateException("Watermark processing and overlay cleanup both failed.", processingError, cleanup);
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
