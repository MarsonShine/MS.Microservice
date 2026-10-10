namespace MS.Microservice.Media.Inference.WatermarkRemoval;

public enum WatermarkRemovalMode { Automatic, Manual }
public enum WatermarkNormalization { RgbZeroToOne, ImageNet }

public sealed record PythonWatermarkDetectorOptions
{
    public required string PythonExecutablePath { get; init; }
    public required string ModelPath { get; init; }
    public WatermarkNormalization Normalization { get; init; } = WatermarkNormalization.RgbZeroToOne;
    public double Threshold { get; init; } = 0.5;
    public int CpuThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    internal string Profile => Normalization == WatermarkNormalization.RgbZeroToOne ? "rgb_0_1" : "imagenet";
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PythonExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelPath);
        if (!Path.IsPathFullyQualified(PythonExecutablePath)) throw new ArgumentException("Configure an absolute, trusted Python executable path.");
        if (!File.Exists(PythonExecutablePath)) throw new FileNotFoundException("The configured Python executable does not exist.", PythonExecutablePath);
        if (!File.Exists(ModelPath)) throw new FileNotFoundException("The configured watermark detector model does not exist.", ModelPath);
        if (!Enum.IsDefined(Normalization) || !double.IsFinite(Threshold) || Threshold is <= 0 or >= 1
            || CpuThreads < 1 || Timeout <= TimeSpan.Zero || Timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentException("Invalid normalization, probability threshold, CPU thread count or finite process timeout.");
    }
}

public sealed record WatermarkRemovalOptions
{
    public required MiganInpaintingOptions Repair { get; init; }
    public PythonWatermarkDetectorOptions? Detector { get; init; }
    public string TemporaryDirectory { get; init; } = Path.GetTempPath();
}

/// <summary>Raw model probabilities are uncalibrated scores, not a guarantee that selected text is a watermark.</summary>
public sealed record WatermarkDetectionResult(string ModelId, string Sha256, string Profile, double Threshold,
    int Width, int Height, int SelectedPixelCount, double MaximumProbability, double MeanProbability,
    double InferenceSeconds, double WorkerSeconds, string Runtime);

/// <summary>Automatic output masks are PNG; manual masks retain their input encoding and orientation metadata.</summary>
public sealed record WatermarkRemovalResult(InpaintingResult Image, WatermarkRemovalMode Mode,
    WatermarkDetectionResult? Detection, bool ReviewRecommended, IReadOnlyList<string> Warnings);
