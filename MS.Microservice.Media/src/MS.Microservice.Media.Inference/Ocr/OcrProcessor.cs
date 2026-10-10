using System.Globalization;
using System.Text.Json;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.WatermarkRemoval;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Media.Inference.Ocr;

/// <summary>Local PP-OCRv5 mobile detection and recognition for normally oriented printed Chinese and English.</summary>
public sealed class OcrProcessor
{
    public const string ModelId = "RapidAI/PP-OCRv5-mobile@v3.9.2";
    internal const string DetectionFile = "ch_PP-OCRv5_det_mobile.onnx";
    internal const string RecognitionFile = "ch_PP-OCRv5_rec_mobile.onnx";
    internal const string DictionaryFile = "ppocrv5_dict.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly OcrOptions _options;

    public OcrProcessor(OcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PythonExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TemporaryDirectory);
        ImageCodec.ValidateLimits(options.Limits);
        if (!Path.IsPathFullyQualified(options.PythonExecutablePath))
            throw new ArgumentException("Configure an absolute, trusted Python executable path.", nameof(options));
        if (!File.Exists(options.PythonExecutablePath))
            throw new FileNotFoundException("The configured Python executable does not exist.", options.PythonExecutablePath);
        foreach (var name in new[] { DetectionFile, RecognitionFile, DictionaryFile })
            if (!File.Exists(Path.Combine(options.ModelDirectory, name)))
                throw new FileNotFoundException("A required local OCR artifact is missing.", Path.Combine(options.ModelDirectory, name));
        if (!Directory.Exists(options.TemporaryDirectory))
            throw new DirectoryNotFoundException("Configure an existing trusted temporary directory.");
        if (options.CpuThreads is < 1 or > 64 || options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > uint.MaxValue - 1
            || options.DetectionMaximumSide is < 32 or > 2048 || options.DetectionMaximumSide % 32 != 0
            || options.MaximumRecognitionWidth is < 320 or > 4096
            || options.MaximumRegions is < 1 or > 256 || options.MaximumTextCharacters is < 1 or > 32_768 || options.MaximumIntermediateBytes < 1
            || !Probability(options.DetectionThreshold) || options.DetectionThreshold is 0 or 1
            || !Probability(options.BoxThreshold) || !Probability(options.MinimumConfidence))
            throw new ArgumentException("Invalid OCR thresholds, CPU threads, timeout or inference budgets.", nameof(options));
        _options = options with
        {
            ModelDirectory = Path.GetFullPath(options.ModelDirectory), TemporaryDirectory = Path.GetFullPath(options.TemporaryDirectory),
            Limits = options.Limits with { }
        };
    }

    /// <summary>Reads from the current stream position and keeps the caller's stream open. Every call starts a bounded CPU worker.</summary>
    public async Task<OcrResult> RecognizeAsync(Stream inputImage, CancellationToken cancellationToken = default)
    {
        using var image = await ImageCodec.DecodeAsync(inputImage, _options.Limits, cancellationToken).ConfigureAwait(false);
        var workspace = new RemovalWorkspace(_options.TemporaryDirectory);
        Exception? failure = null;
        try
        {
            var normalized = Path.Combine(workspace.DirectoryPath, "normalized.png");
            await using (var output = new ScratchStream(normalized, _options.MaximumIntermediateBytes))
                await ImageCodec.EncodeAsync(image, output, ImageOutputFormat.Png, 100, new(255, 255, 255), cancellationToken).ConfigureAwait(false);
            var script = Path.Combine(AppContext.BaseDirectory, "media-inference", "ocr_worker.py");
            if (!File.Exists(script)) throw new FileNotFoundException("The packaged OCR worker is missing.", script);
            var arguments = new List<string> { "-B", script, "--input", normalized, "--models", _options.ModelDirectory };
            Add("--threads", _options.CpuThreads);
            Add("--max-pixels", _options.Limits.MaximumPixels);
            Add("--max-dimension", _options.Limits.MaximumDimension);
            Add("--det-side", _options.DetectionMaximumSide);
            Add("--rec-width", _options.MaximumRecognitionWidth);
            Add("--max-regions", _options.MaximumRegions);
            Add("--max-characters", _options.MaximumTextCharacters);
            Add("--det-threshold", _options.DetectionThreshold);
            Add("--box-threshold", _options.BoxThreshold);
            Add("--min-confidence", _options.MinimumConfidence);
            var json = await new MediaProcessRunner(_options.Timeout).RunAsync(_options.PythonExecutablePath, arguments, cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<OcrResult>(json, JsonOptions) ?? throw new InvalidDataException("The OCR worker returned no result.");
            ValidateResult(result, image.Width, image.Height, _options);
            cancellationToken.ThrowIfCancellationRequested();
            return result with { Regions = Array.AsReadOnly(result.Regions.Select(region => region with { Corners = Array.AsReadOnly(region.Corners.ToArray()) }).ToArray()) };

            void Add(string name, IFormattable value) { arguments.Add(name); arguments.Add(value.ToString(null, CultureInfo.InvariantCulture)); }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { workspace.Delete(); }
            catch (Exception cleanup) when (failure is not null) { throw new AggregateException("OCR failed and its temporary workspace could not be removed.", failure, cleanup); }
        }
    }

    internal static void ValidateResult(OcrResult result, int width, int height, OcrOptions options)
    {
        if (result.Width != width || result.Height != height || result.Regions is null || result.Regions.Count > options.MaximumRegions)
            throw new InvalidDataException("OCR returned invalid dimensions or region count.");
        long characters = 0;
        foreach (var region in result.Regions)
        {
            if (region is null || string.IsNullOrEmpty(region.Text) || !Probability(region.Confidence)
                || region.Confidence < options.MinimumConfidence || region.Corners is null || region.Corners.Count != 4)
                throw new InvalidDataException("OCR returned invalid text, confidence or corners.");
            characters += region.Text.Length;
            if (characters > options.MaximumTextCharacters || region.Corners.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)
                || p.X < 0 || p.X > width - 1 || p.Y < 0 || p.Y > height - 1))
                throw new InvalidDataException("OCR returned text or geometry outside configured limits.");
        }
    }

    private static bool Probability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private sealed class ScratchStream(string path, int maximumBytes) : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)
    {
        private void Check(int count)
        {
            if (Position > (long)maximumBytes - count)
                throw new InvalidDataException("The normalized OCR PNG exceeds the intermediate byte budget.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
    }
}
