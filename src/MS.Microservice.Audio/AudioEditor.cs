using NAudio.Lame;
using NAudio.Wave;

namespace MS.Microservice.Infrastructure.Common.NAudio;

public sealed class AudioEditor
{
    /// <summary>
    /// Trims whole sample frames and applies gain/fades without resampling or changing channels.
    /// Publishes a new WAV or MP3 after successful finalization; never overwrites an existing file.
    /// </summary>
    /// <param name="progress">Decoded fraction; 1 means decoding finished, before output publication.</param>
    public Task EditAsync(
        string inputFile,
        string outputFile,
        AudioEditOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);
        var inputPath = Path.GetFullPath(inputFile);
        var outputPath = Path.GetFullPath(outputFile);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(inputPath, outputPath, comparison))
            throw new ArgumentException("Input and output must be different paths.", nameof(outputFile));
        var extension = Path.GetExtension(outputPath);
        var mp3 = extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        if (!mp3 && !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Audio editing outputs WAV or MP3 files.");

        options ??= new AudioEditOptions();
        if (options.Start < TimeSpan.Zero || options.Duration is { } duration && duration <= TimeSpan.Zero
            || options.FadeIn < TimeSpan.Zero || options.FadeOut < TimeSpan.Zero
            || !float.IsFinite(options.Gain) || options.Gain < 0
            || mp3 && !Enum.IsDefined(options.Mp3Preset))
            throw new ArgumentException("Use nonnegative times and finite gain, a positive duration and a valid MP3 preset.", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Edit(inputPath, outputPath, mp3, options, progress, cancellationToken), cancellationToken);
    }

    private static void Edit(
        string inputPath,
        string outputPath,
        bool mp3,
        AudioEditOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".audio-{Guid.NewGuid():N}.tmp");
        var ownsTemporaryFile = false;
        try
        {
            using (var reader = new AudioFileReader(inputPath))
            {
                var format = reader.WaveFormat;
                var channels = format.Channels;
                var frameBytes = checked(channels * sizeof(float));
                if (channels < 1 || reader.Length % frameBytes != 0)
                    throw new InvalidDataException("The decoded audio does not contain complete sample frames.");
                if (mp3 && channels > 2)
                    throw new NotSupportedException("MP3 output supports mono or stereo audio.");
                var totalFrames = reader.Length / frameBytes;
                var start = ToFrames(options.Start, format.SampleRate);
                if (start >= totalFrames)
                    throw new ArgumentException("Start must select an audio frame before the end of input.", nameof(options));
                var frames = options.Duration is { } duration
                    ? Math.Min(ToFrames(duration, format.SampleRate), totalFrames - start)
                    : totalFrames - start;
                var fadeIn = ToFrames(options.FadeIn, format.SampleRate);
                var fadeOut = ToFrames(options.FadeOut, format.SampleRate);
                if (frames < 1 || fadeIn > frames || fadeOut > frames)
                    throw new ArgumentException("The clip must contain frames and each fade must fit within it.", nameof(options));
                if (File.Exists(outputPath))
                    throw new IOException("The output file already exists.");
                cancellationToken.ThrowIfCancellationRequested();
                reader.Position = checked(start * frameBytes);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                // Ownership starts only after CreateNew succeeds, even if a writer constructor fails.
                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                ownsTemporaryFile = true;
                Stream writer = mp3
                    ? new LameMP3FileWriter(output, format, options.Mp3Preset)
                    : new WaveFileWriter(output, format);
                try
                {
                    Process(reader, writer, frames, fadeIn, fadeOut, options.Gain, progress, cancellationToken);
                }
                catch (Exception processing)
                {
                    try { writer.Dispose(); }
                    catch (Exception finalization)
                    {
                        throw new AggregateException("Audio processing and encoder finalization both failed.", processing, finalization);
                    }

                    throw;
                }

                writer.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: false);
        }
        catch (Exception primary)
        {
            if (ownsTemporaryFile)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new AggregateException("Audio processing and temporary file cleanup both failed.", primary, cleanup);
                }
            }

            throw;
        }
    }

    private static void Process(
        AudioFileReader reader, Stream writer, long frames, long fadeIn, long fadeOut,
        float volume, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var channels = reader.WaveFormat.Channels;
        var samples = new float[Math.Max(1, 4096 / channels) * channels];
        var bytes = new byte[samples.Length * sizeof(float)];
        var processed = 0L;
        progress?.Report(0);
        while (processed < frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = checked((int)(Math.Min(frames - processed, samples.Length / channels) * channels));
            var read = reader.Read(samples, 0, requested);
            if (read == 0)
                throw new EndOfStreamException("The decoder ended before the requested clip was complete.");
            if (read % channels != 0)
                throw new InvalidDataException("The decoder returned an incomplete sample frame.");
            var readFrames = read / channels;
            for (var frame = 0; frame < readFrames; frame++)
            {
                var position = processed + frame;
                var remaining = frames - position - 1;
                double gain = volume;
                if (position < fadeIn)
                    gain *= fadeIn == 1 ? 0 : (double)position / (fadeIn - 1);
                if (remaining < fadeOut)
                    gain *= fadeOut == 1 ? 0 : (double)remaining / (fadeOut - 1);
                for (var channel = 0; channel < channels; channel++)
                {
                    var index = frame * channels + channel;
                    if (!float.IsFinite(samples[index]))
                        throw new InvalidDataException("Audio samples must be finite.");
                    samples[index] = (float)Math.Clamp(samples[index] * gain, -1, 1);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(samples, 0, bytes, 0, read * sizeof(float));
            writer.Write(bytes, 0, read * sizeof(float));
            processed += readFrames;
            progress?.Report((double)processed / frames);
        }
    }

    private static long ToFrames(TimeSpan time, int sampleRate)
    {
        var frames = (decimal)time.Ticks * sampleRate / TimeSpan.TicksPerSecond;
        if (frames > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(time), "The requested time exceeds the supported frame count.");
        return (long)frames;
    }
}
