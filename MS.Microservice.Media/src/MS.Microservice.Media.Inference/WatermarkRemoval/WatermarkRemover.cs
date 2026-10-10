using System.Globalization;
using System.Text.Json;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Media.Inference.WatermarkRemoval;

/// <summary>Detects repeated-text watermarks by default and repairs selected regions with MI-GAN. Callers retain originals and supply separate output resources.</summary>
public sealed class WatermarkRemover : IAsyncDisposable
{
    public const string DetectorModelId = "UNet++/efficientnet-b4/repeated-text@509fb6833823f4e9c519f8e4f87a9053567c779c";
    public const string DetectorSha256 = "bfd26cffb00d49541438b66e5723edaa733b60e61b09bf6abca9a5ba1b738d0e";
    private readonly WatermarkRemovalOptions _options;
    private readonly MiganInpainter _repair;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;
    private bool _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private WatermarkRemover(WatermarkRemovalOptions options, MiganInpainter repair) { _options = options; _repair = repair; }

    public static async Task<WatermarkRemover> CreateAsync(WatermarkRemovalOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Repair);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TemporaryDirectory);
        options.Detector?.Validate();
        var normalized = options with
        {
            TemporaryDirectory = Path.GetFullPath(options.TemporaryDirectory),
            Detector = options.Detector is { } detector ? detector with { ModelPath = Path.GetFullPath(detector.ModelPath) } : null
        };
        return new WatermarkRemover(normalized, await MiganInpainter.CreateAsync(options.Repair, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Consumes caller streams at their current positions; keeps all streams open. Outputs may be partial on failure.</summary>
    public async Task<WatermarkRemovalResult> RemoveAsync(Stream inputImage, Stream outputPng, Stream outputMask,
        WatermarkRemovalMode mode = WatermarkRemovalMode.Automatic, Stream? manualMask = null, CancellationToken cancellationToken = default)
    {
        ValidateRequest(inputImage, outputPng, outputMask, mode, manualMask);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        RemovalWorkspace? workspace = null;
        var failed = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            if (mode == WatermarkRemovalMode.Automatic && _options.Detector is null)
                throw new InvalidOperationException("Automatic removal requires a configured local Python detector and verified model; choose Manual with a binary mask to use manual repair.");
            workspace = new RemovalWorkspace(_options.TemporaryDirectory);
            var limits = _options.Repair.Limits;
            await RemovalWorkspace.SnapshotAsync(inputImage, workspace.ImagePath, limits.MaximumEncodedBytes, cancellationToken).ConfigureAwait(false);
            WatermarkDetectionResult? detection = null;
            if (mode == WatermarkRemovalMode.Manual)
                await RemovalWorkspace.SnapshotAsync(manualMask!, workspace.MaskPath, limits.MaximumEncodedBytes, cancellationToken).ConfigureAwait(false);
            else detection = await DetectAsync(workspace, _options.Detector!, cancellationToken).ConfigureAwait(false);
            if (new FileInfo(workspace.MaskPath).Length > limits.MaximumEncodedBytes)
                throw new InvalidDataException("The generated mask exceeds the configured encoded mask byte limit.");
            await using var image = File.OpenRead(workspace.ImagePath);
            await using var mask = File.OpenRead(workspace.MaskPath);
            var result = await _repair.InpaintAsync(image, mask, outputPng, cancellationToken).ConfigureAwait(false);
            if (detection is not null && (detection.Width != result.Width || detection.Height != result.Height || detection.SelectedPixelCount != result.SelectedPixelCount))
                throw new InvalidDataException("Detector metadata does not match the normalized image and binary mask.");
            mask.Position = 0;
            await mask.CopyToAsync(outputMask, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var warning = detection is null ? "Repair synthesizes content inside the selected mask; review the result."
                : "Automatic masks can include ordinary text, subtitles or logos; review the original, mask and result and adjust the mask when needed.";
            return new(result, mode, detection, true, Array.AsReadOnly(new[] { warning }));
        }
        catch { failed = true; throw; }
        finally
        {
            try { workspace?.Delete(); }
            catch when (failed) { }
            finally { _gate.Release(); }
        }
    }

    private async Task<WatermarkDetectionResult> DetectAsync(RemovalWorkspace workspace, PythonWatermarkDetectorOptions detector, CancellationToken cancellationToken)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "media-inference", "watermark_detector.py");
        if (!File.Exists(script)) throw new FileNotFoundException("The packaged watermark detector worker is missing from the application output.", script);
        var limits = _options.Repair.Limits;
        var json = await new MediaProcessRunner(detector.Timeout).RunAsync(detector.PythonExecutablePath,
        [script, "--input", workspace.ImagePath, "--mask-output", workspace.MaskPath, "--model", detector.ModelPath,
            "--profile", detector.Profile, "--threshold", detector.Threshold.ToString("R", CultureInfo.InvariantCulture),
            "--threads", detector.CpuThreads.ToString(CultureInfo.InvariantCulture), "--max-pixels", limits.MaximumPixels.ToString(CultureInfo.InvariantCulture),
            "--max-dimension", limits.MaximumDimension.ToString(CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<WatermarkDetectionResult>(json, JsonOptions) ?? throw new InvalidDataException("The detector returned no metadata.");
        if (result.ModelId != DetectorModelId || result.Sha256 != DetectorSha256 || result.Profile != detector.Profile || result.Threshold != detector.Threshold
            || result.Width < 1 || result.Height < 1 || (long)result.Width * result.Height > limits.MaximumPixels
            || result.SelectedPixelCount < 0 || result.SelectedPixelCount > (long)result.Width * result.Height
            || !double.IsFinite(result.MaximumProbability) || !double.IsFinite(result.MeanProbability)
            || result.MaximumProbability is < 0 or > 1 || result.MeanProbability is < 0 or > 1)
            throw new InvalidDataException("The detector returned invalid model, profile, dimensions or uncalibrated probability metadata.");
        return result;
    }

    internal static void ValidateRequest(Stream input, Stream imageOutput, Stream maskOutput, WatermarkRemovalMode mode, Stream? manualMask)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(imageOutput);
        ArgumentNullException.ThrowIfNull(maskOutput);
        if (!Enum.IsDefined(mode) || mode == WatermarkRemovalMode.Manual && manualMask is null || mode == WatermarkRemovalMode.Automatic && manualMask is not null)
            throw new ArgumentException("Choose Automatic without a mask, or Manual with a binary mask.");
        Stream[] streams = manualMask is null ? [input, imageOutput, maskOutput] : [input, imageOutput, maskOutput, manualMask];
        if (streams.Distinct(ReferenceEqualityComparer.Instance).Count() != streams.Length)
            throw new ArgumentException("Input, outputs and manual mask must be distinct streams.");
        if (!input.CanRead || manualMask is { CanRead: false } || !imageOutput.CanWrite || !maskOutput.CanWrite)
            throw new ArgumentException("Inputs must be readable and outputs writable.");
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await _repair.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
