using System.Globalization;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed partial class VideoProcessor
{
    /// <summary>Trims, transforms and re-encodes a local video, publishing a new file only on success.</summary>
    public async Task<VideoInfo> ProcessAsync(string inputFile, string outputFile,
        VideoEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);
        var input = Path.GetFullPath(inputFile);
        var output = Path.GetFullPath(outputFile);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (input.Equals(output, comparison))
            throw new ArgumentException("Input and output must be different files.", nameof(outputFile));
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("The output already exists.");
        var extension = Path.GetExtension(output).ToLowerInvariant();
        if (extension is not (".mp4" or ".mkv" or ".webm"))
            throw new NotSupportedException("Video processing outputs MP4, MKV or WebM.");
        options ??= new VideoEditOptions();
        ValidateEditOptions(options);
        var info = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
        var filters = BuildFilters(info, options);
        if (info.Duration is { } length && options.Start >= length)
            throw new ArgumentException("The start must precede the end of the video.", nameof(options));

        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input };
        if (options.Start > TimeSpan.Zero) arguments.AddRange(["-ss", Seconds(options.Start)]);
        if (options.Duration is { } duration) arguments.AddRange(["-t", Seconds(duration)]);
        arguments.AddRange(["-map", "0:V:0", "-map", "0:a?", "-vf", filters,
            "-metadata:s:v:0", "rotate=0", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough"]);
        var crf = options.ConstantRateFactor.ToString(CultureInfo.InvariantCulture);
        if (extension == ".webm")
            arguments.AddRange(["-c:v", "libvpx-vp9", "-crf", crf, "-b:v", "0", "-cpu-used",
                options.Speed switch { VideoEncodingSpeed.Fast => "4", VideoEncodingSpeed.Balanced => "2", _ => "0" }, "-c:a", "libopus"]);
        else
            arguments.AddRange(["-c:v", "libx264", "-crf", crf, "-preset",
                options.Speed switch { VideoEncodingSpeed.Fast => "veryfast", VideoEncodingSpeed.Balanced => "medium", _ => "slow" },
                "-c:a", "aac", "-b:a", "192k"]);
        if (extension == ".mp4") arguments.AddRange(["-movflags", "+faststart"]);

        return await PublishFileAsync(output, extension, async temporary =>
        {
            arguments.Add(temporary);
            await _runner.RunAsync(_options.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
            var result = await ProbeAsync(temporary, cancellationToken).ConfigureAwait(false);
            CheckVideoDimensions(result.Width, result.Height, options);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateEditOptions(VideoEditOptions options)
    {
        if (options.Start < TimeSpan.Zero || options.Duration is { } duration && duration <= TimeSpan.Zero
            || options.RotationDegrees is not (0 or 90 or 180 or 270)
            || options.ConstantRateFactor is < 0 or > 51 || !Enum.IsDefined(options.Speed)
            || !Enum.IsDefined(options.ResizeMode) || options.MaximumDimension < 1 || options.MaximumPixels < 1)
            throw new ArgumentException("Invalid video time, transform, quality, speed or dimension limits.", nameof(options));
        if (options.Resize is { } size)
        {
            CheckVideoDimensions(size.Width, size.Height, options);
            CheckVideoDimensions((long)size.Width + size.Width % 2, (long)size.Height + size.Height % 2, options);
        }
    }

    private static string BuildFilters(VideoInfo input, VideoEditOptions options)
    {
        CheckVideoDimensions(input.Width, input.Height, options);
        if (options.Resize is null)
        {
            long width = options.Crop?.Width ?? input.Width;
            long height = options.Crop?.Height ?? input.Height;
            if (options.RotationDegrees is 90 or 270) (width, height) = (height, width);
            CheckVideoDimensions(width + width % 2, height + height % 2, options);
        }
        var filters = new List<string>();
        if (options.Crop is { } crop)
        {
            if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1
                || (long)crop.X + crop.Width > input.Width || (long)crop.Y + crop.Height > input.Height)
                throw new ArgumentException("The crop must lie within the oriented video raster.", nameof(options));
            filters.Add(FormattableString.Invariant($"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1"));
        }
        switch (options.RotationDegrees)
        {
            case 90: filters.Add("transpose=clock"); break;
            case 180: filters.AddRange(["hflip", "vflip"]); break;
            case 270: filters.Add("transpose=cclock"); break;
        }
        if (options.FlipHorizontal) filters.Add("hflip");
        if (options.FlipVertical) filters.Add("vflip");
        if (options.Resize is { } size)
        {
            var dimensions = FormattableString.Invariant($"scale={size.Width}:{size.Height}");
            if (options.ResizeMode == ImageResizeMode.Contain)
                filters.Add(dimensions + ":force_original_aspect_ratio=decrease");
            else if (options.ResizeMode == ImageResizeMode.Cover)
            {
                filters.Add(dimensions + ":force_original_aspect_ratio=increase");
                filters.Add(FormattableString.Invariant($"crop={size.Width}:{size.Height}:(iw-ow)/2:(ih-oh)/2:exact=1"));
            }
            else filters.Add(dimensions);
            filters.Add("setsar=1");
        }
        // YUV420 encoders need even dimensions; retain edge pixels by padding, never cropping them off.
        filters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
        return string.Join(',', filters);
    }

    private static void CheckVideoDimensions(long width, long height, VideoEditOptions options)
    {
        if (width < 1 || height < 1 || width > options.MaximumDimension || height > options.MaximumDimension
            || (long)width * height > options.MaximumPixels)
            throw new InvalidDataException("Video dimensions exceed the configured limits.");
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.#######", CultureInfo.InvariantCulture);

    private static async Task<T> PublishFileAsync<T>(string output, string extension,
        Func<string, Task<T>> produce, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".video-" + Guid.NewGuid().ToString("N") + extension);
        using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            var info = await produce(temporary).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: false);
            return info;
        }
        catch (Exception primary)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                throw new AggregateException("Video processing and temporary-file cleanup both failed.", primary, cleanup);
            }
            throw;
        }
    }
}
