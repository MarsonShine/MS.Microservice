using System.Globalization;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed partial class VideoProcessor
{
    /// <summary>Normalizes and re-encodes heterogeneous videos in their supplied order.</summary>
    public async Task<VideoInfo> ConcatenateAsync(IReadOnlyList<string> inputFiles, string outputFile,
        VideoConcatenationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new VideoConcatenationOptions();
        if (options.MaximumInputs < 1 || options.FrameRate is { } requested && !ValidConcatenationRate(requested))
            throw new ArgumentException("Choose a positive input limit and frame rate from 0 (exclusive) to 240.", nameof(options));
        var count = inputFiles.Count;
        if (count < 1 || count > options.MaximumInputs)
            throw new ArgumentException("Choose a bounded nonempty input list.", nameof(inputFiles));
        var limits = new VideoEditOptions
        {
            Resize = options.Canvas, MaximumDimension = options.MaximumDimension, MaximumPixels = options.MaximumPixels,
            ConstantRateFactor = options.ConstantRateFactor, Speed = options.Speed
        };
        ValidateEditOptions(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);
        var target = Path.GetFullPath(outputFile);
        var paths = new string[count];
        for (var index = 0; index < count; index++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inputFiles[index], nameof(inputFiles));
            paths[index] = Path.GetFullPath(inputFiles[index]);
            if (paths[index].Equals(target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Output must differ from every input file.", nameof(outputFile));
        }
        var (_, output, extension) = ValidateAssetPaths(paths[0], target);
        if (extension is not (".mp4" or ".mkv" or ".webm"))
            throw new NotSupportedException("Video concatenation outputs MP4, MKV or WebM.");
        var sources = new VideoInfo[paths.Length];
        for (var index = 0; index < paths.Length; index++)
        {
            sources[index] = await ProbeAsync(paths[index], cancellationToken).ConfigureAwait(false);
            CheckVideoDimensions(sources[index].Width, sources[index].Height, limits);
            if (sources[index].Duration is not { } duration || duration <= TimeSpan.Zero)
                throw new NotSupportedException("Every concatenated segment needs a known positive container duration.");
        }
        var fps = options.FrameRate ?? sources[0].FrameRate
            ?? throw new NotSupportedException("The first frame rate is unknown; provide an explicit output frame rate.");
        if (!ValidConcatenationRate(fps)) throw new ArgumentException("The output frame rate must be finite and between 0 (exclusive) and 240.");
        var canvas = options.Canvas ?? new ImageSize(sources[0].Width, sources[0].Height);
        long paddedWidth = (long)canvas.Width + canvas.Width % 2;
        long paddedHeight = (long)canvas.Height + canvas.Height % 2;
        CheckVideoDimensions(paddedWidth, paddedHeight, limits);
        canvas = new((int)paddedWidth, (int)paddedHeight);
        var rate = fps.ToString("R", CultureInfo.InvariantCulture);
        var withAudio = sources.Any(source => source.HasAudio);
        var filters = new List<string>();
        var labels = new List<string>();
        for (var index = 0; index < sources.Length; index++)
        {
            var duration = QuantizeSegmentDuration(sources[index].Duration!.Value, fps);
            var time = Seconds(duration);
            var label = index.ToString(CultureInfo.InvariantCulture);
            // Use display aspect ratio directly: neither scale dimension can exceed the checked canvas.
            var scale = FormattableString.Invariant($"scale=w='max(2,trunc(min({canvas.Width},{canvas.Height}*dar)/2)*2)':h='max(2,trunc(min({canvas.Height},{canvas.Width}/dar)/2)*2)'");
            var pad = FormattableString.Invariant($"pad={canvas.Width}:{canvas.Height}:(ow-iw)/2:(oh-ih)/2:color=black");
            filters.Add($"[{label}:V:0]setpts=PTS-STARTPTS,{scale},setsar=1,{pad}," +
                $"fps={rate},tpad=stop_mode=clone:stop_duration={time},trim=duration={time}," +
                $"setpts=PTS-STARTPTS,format=yuv420p[v{label}]");
            labels.Add($"[v{label}]");
            if (!withAudio) continue;
            filters.Add(sources[index].HasAudio
                ? $"[{label}:a:0]asetpts=PTS-STARTPTS,aresample=48000," +
                    $"aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo," +
                    $"apad=whole_dur={time},atrim=duration={time},asetpts=PTS-STARTPTS[a{label}]"
                : $"anullsrc=r=48000:cl=stereo,atrim=duration={time},asetpts=PTS-STARTPTS[a{label}]");
            labels.Add($"[a{label}]");
        }
        filters.Add(string.Concat(labels) + $"concat=n={paths.Length.ToString(CultureInfo.InvariantCulture)}:v=1:a={(withAudio ? 1 : 0)}[joinedVideo]" +
            (withAudio ? "[audio]" : ""));
        filters.Add($"[joinedVideo]{ClearAppliedRotationFilter}[video]");
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-filter_complex_threads", "1" };
        foreach (var path in paths) arguments.AddRange(["-threads", "1", "-i", path]);
        arguments.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[video]"]);
        if (withAudio) arguments.AddRange(["-map", "[audio]", "-ar", "48000", "-ac", "2"]);
        else arguments.Add("-an");
        arguments.AddRange(["-metadata:s:v:0", "rotate=0", "-pix_fmt", "yuv420p", "-r", rate, "-fps_mode", "cfr"]);
        AddVideoEncodingArguments(arguments, extension, options.ConstantRateFactor, options.Speed);
        return await PublishFileAsync(output, extension, async temporary =>
        {
            arguments.Add(temporary);
            await _runner.RunAsync(_options.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
            var result = await ProbeAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (result.Width != canvas.Width || result.Height != canvas.Height || result.HasAudio != withAudio || result.RotationDegrees != 0)
                throw new InvalidDataException("The concatenated output does not match its normalized canvas/audio contract.");
            if (withAudio)
            {
                var audio = await ProbeAudioAssetAsync(temporary, 0, true, cancellationToken).ConfigureAwait(false);
                if (audio.SampleRate != 48_000 || audio.Channels != 2)
                    throw new InvalidDataException("The concatenated audio was not normalized to 48 kHz stereo.");
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool ValidConcatenationRate(double rate) => double.IsFinite(rate) && rate > 0 && rate <= 240;

    private static TimeSpan QuantizeSegmentDuration(TimeSpan duration, double rate)
    {
        var seconds = Math.Ceiling(duration.TotalSeconds * rate) / rate;
        if (!double.IsFinite(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds)
            throw new NotSupportedException("The segment duration cannot be represented on the requested frame grid.");
        return TimeSpan.FromSeconds(seconds);
    }
}
