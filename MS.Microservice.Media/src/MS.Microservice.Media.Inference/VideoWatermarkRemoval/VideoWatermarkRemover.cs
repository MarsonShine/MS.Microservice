using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.CompilerServices;
using MS.Microservice.Media.Inference.WatermarkRemoval;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Media.Inference.VideoWatermarkRemoval;

/// <summary>Local MI-GAN video repair with optical-flow-assisted adjacent frames and a lossless, atomically published result bundle.</summary>
public sealed class VideoWatermarkRemover : IAsyncDisposable
{
    private readonly VideoWatermarkRemovalOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public VideoWatermarkRemover(VideoWatermarkRemovalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options with { MiganModelPath = Path.GetFullPath(options.MiganModelPath),
            Detector = options.Detector is { } detector ? detector with { ModelPath = Path.GetFullPath(detector.ModelPath) } : null };
    }

    /// <summary>Detects watermarks automatically, then performs the same model repair and validated bundle publication as manual requests.</summary>
    [OverloadResolutionPriority(-1)]
    public Task<VideoWatermarkRemovalResult> RemoveAsync(string inputVideoPath, string outputDirectory, CancellationToken cancellationToken = default) =>
        RemoveCoreAsync(inputVideoPath, outputDirectory, null, cancellationToken);

    /// <summary>Requires a new output directory and stable regular input files. No result directory is published on cancellation or validation failure.</summary>
    public Task<VideoWatermarkRemovalResult> RemoveAsync(string inputVideoPath, string outputDirectory,
        VideoWatermarkRemovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RemoveCoreAsync(inputVideoPath, outputDirectory, request, cancellationToken);
    }

    private async Task<VideoWatermarkRemovalResult> RemoveCoreAsync(string inputVideoPath, string outputDirectory,
        VideoWatermarkRemovalRequest? request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputVideoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
        var automatic = request is null;
        if (automatic && _options.Detector is null)
            throw new InvalidOperationException("Automatic video removal requires a configured local repeated-text detector checkpoint; use an explicit manual request for mask-based repair.");
        var input = Path.GetFullPath(inputVideoPath);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("The output must have a parent directory.");
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The output directory must not already exist.");
        var maskRequest = request?.Snapshot(_options.Limits);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        RemovalWorkspace? workspace = null;
        var failed = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            var script = Path.Combine(AppContext.BaseDirectory, "media-inference", "video_watermark_worker.py");
            if (!File.Exists(script)) throw new FileNotFoundException("The packaged video repair worker is missing.", script);
            Directory.CreateDirectory(parent);
            workspace = new RemovalWorkspace(parent);
            var bundle = Path.Combine(workspace.DirectoryPath, "bundle");
            Directory.CreateDirectory(bundle);
            await using var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            var originalInfo = new FileInfo(input);
            var originalTime = originalInfo.LastWriteTimeUtc;
            var originalLength = source.Length;
            if ((originalInfo.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new ArgumentException("The video must be a regular file rather than a directory or symbolic link.");
            var originalHash = await SnapshotAsync(source, workspace.ImagePath, _options.Limits.MaximumInputBytes, cancellationToken).ConfigureAwait(false);
            if (request?.StaticMaskPath is not null)
            {
                await using var mask = File.OpenRead(Path.GetFullPath(request.StaticMaskPath));
                await SnapshotAsync(mask, workspace.MaskPath, _options.Limits.MaximumInputBytes, cancellationToken).ConfigureAwait(false);
                maskRequest = new(workspace.MaskPath, null);
            }
            var jobPath = Path.Combine(workspace.DirectoryPath, "job.json");
            var job = new { InputPath = workspace.ImagePath, OutputDirectory = bundle, ModelPath = _options.MiganModelPath,
                _options.CpuThreads, _options.Limits, _options.Flow, Request = maskRequest, Mode = automatic ? "automatic" : "manual",
                Detector = automatic ? new { _options.Detector!.ModelPath, _options.Detector.Profile, _options.Detector.Threshold } : null };
            await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job, JsonOptions), cancellationToken).ConfigureAwait(false);
            var runner = new MediaProcessRunner(_options.ProcessTimeout);
            await runner.RunAsync(_options.PythonExecutablePath, [script, "prepare", jobPath], cancellationToken).ConfigureAwait(false);
            var videoOnly = Path.Combine(bundle, "video-only.mkv");
            var remaining = _options.Limits.MaximumOutputBytes - BundleBytes(bundle) + new FileInfo(videoOnly).Length;
            if (remaining <= 0) throw new InvalidDataException("Video previews already exhaust the result bundle byte budget.");
            await runner.RunAsync(_options.FfmpegExecutablePath,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-copyts", "-i", videoOnly, "-i", workspace.ImagePath,
                "-map", "0:v:0", "-map", "1:a?", "-map_metadata", "-1", "-map_chapters", "-1", "-c:v", "copy", "-c:a", "pcm_f32le",
                "-metadata:s:v:0", "rotate=0", "-avoid_negative_ts", "disabled", "-fs", remaining.ToString(CultureInfo.InvariantCulture),
                Path.Combine(bundle, "result.mkv")], cancellationToken).ConfigureAwait(false);
            var json = await runner.RunAsync(_options.PythonExecutablePath, [script, "validate", jobPath], cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Deserialize<WorkerSummary>(json, JsonOptions) ?? throw new InvalidDataException("The video worker returned no result.");
            ValidateDetection(summary.Mode, summary.Detection, summary.FrameCount, automatic);
            if (summary.MiganModelId != MiganInpainter.ModelId || !string.Equals(summary.MiganSha256, MiganInpainter.ModelSha256, StringComparison.OrdinalIgnoreCase)
                || summary.Width < 1 || summary.Height < 1 || (long)summary.Width * summary.Height > _options.Limits.MaximumFramePixels
                || summary.FrameCount < 1 || summary.FrameCount > _options.Limits.MaximumFrames
                || (long)summary.Width * summary.Height * summary.FrameCount > _options.Limits.MaximumTotalPixels
                || summary.SelectedPixelCount < 0 || summary.SelectedPixelCount > (long)summary.Width * summary.Height * summary.FrameCount
                || summary.EditedPixelCount < 0 || summary.EditedPixelCount > summary.SelectedPixelCount
                || summary.FlowContributedPixelCount < 0 || summary.FlowContributedPixelCount > 2 * summary.SelectedPixelCount
                || summary.Audio is null || summary.Audio.Length > _options.Limits.MaximumAudioTracks
                || !double.IsFinite(summary.FirstVideoSeconds) || summary.FirstVideoSeconds < 0
                || !double.IsFinite(summary.VideoDurationSeconds) || summary.VideoDurationSeconds <= 0 || summary.VideoDurationSeconds > _options.Limits.MaximumDurationSeconds)
                throw new InvalidDataException("The video worker returned invalid result dimensions or frame count.");
            File.Delete(videoOnly);
            var bytes = BundleBytes(bundle);
            if (bytes > _options.Limits.MaximumOutputBytes) throw new InvalidDataException("The complete video bundle exceeds its byte budget.");
            source.Position = 0;
            var finalHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
            originalInfo.Refresh();
            if (originalLength != source.Length || originalLength != originalInfo.Length || originalTime != originalInfo.LastWriteTimeUtc || originalHash != finalHash)
                throw new IOException("The source changed while the video was processed; keep input files stable until completion.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(bundle, output);
            return new(input, originalHash, output, summary.Width, summary.Height, summary.FrameCount, summary.SelectedPixelCount,
                summary.EditedPixelCount, summary.FlowContributedPixelCount, summary.FirstVideoSeconds, summary.VideoDurationSeconds,
                bytes, Array.AsReadOnly(summary.Audio), true, Array.AsReadOnly(new[] {
                    "MI-GAN synthesizes selected content; optical flow can worsen repair despite reliability gates. Review original, mask and result.",
                    "Output is lossless RGB8 FFV1 Matroska with audio decoded to float32 PCM; timestamps are quantized to 1 ms.",
                    "Audio samples preserve channel order. Source speaker roles are recorded in the manifest; Matroska PCM playback may omit those roles.",
                    automatic ? "Automatic repeated-text masks can select ordinary text, subtitles or scene logos and miss watermark ink; review masks before accepting the result." : "Manual masks define the selected region; inspect synthesized content before accepting the result."
                }), automatic ? WatermarkRemovalMode.Automatic : WatermarkRemovalMode.Manual, summary.Detection);
        }
        catch { failed = true; throw; }
        finally
        {
            try { workspace?.Delete(); }
            catch when (failed) { }
            finally { _gate.Release(); }
        }
    }

    private void ValidateDetection(string mode, VideoWatermarkDetectionResult? detection, int frames, bool automatic)
    {
        if (mode != (automatic ? "automatic" : "manual") || !automatic && detection is not null)
            throw new InvalidDataException("The video worker changed the requested manual/automatic mode.");
        if (!automatic) return;
        var configured = _options.Detector!;
        if (detection is null || detection.ModelId != WatermarkRemover.DetectorModelId || detection.Sha256 != WatermarkRemover.DetectorSha256
            || detection.Profile != configured.Profile || detection.Threshold != configured.Threshold || detection.FrameCount != frames
            || !double.IsFinite(detection.MaximumProbability) || detection.MaximumProbability is < 0 or > 1
            || !double.IsFinite(detection.MeanProbability) || detection.MeanProbability is < 0 or > 1
            || !double.IsFinite(detection.InferenceSeconds) || detection.InferenceSeconds < 0)
            throw new InvalidDataException("The detector returned invalid fixed model, profile or uncalibrated probability metadata.");
    }

    internal static long BundleBytes(string path) => Directory.EnumerateFiles(path).Sum(file => new FileInfo(file).Length);

    internal static async Task<string> SnapshotAsync(Stream source, string destination, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, maximumBytes - total >= buffer.Length ? buffer.Length : (int)(maximumBytes - total + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The input video or mask exceeds its byte budget.");
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (total == 0) throw new InvalidDataException("The input video or mask is empty.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private sealed record WorkerSummary(int Width, int Height, int FrameCount, long SelectedPixelCount, long EditedPixelCount,
        long FlowContributedPixelCount, double FirstVideoSeconds, double VideoDurationSeconds, VideoAudioResult[] Audio, string MiganModelId, string MiganSha256,
        string Mode, VideoWatermarkDetectionResult? Detection);
}
