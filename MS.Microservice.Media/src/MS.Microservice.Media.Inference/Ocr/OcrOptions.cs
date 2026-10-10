using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference.Ocr;

public sealed record OcrOptions
{
    public required string PythonExecutablePath { get; init; }
    public required string ModelDirectory { get; init; }
    public string TemporaryDirectory { get; init; } = Path.GetTempPath();
    public ImageLimits Limits { get; init; } = new();
    public int CpuThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public int DetectionMaximumSide { get; init; } = 1280;
    public int MaximumRecognitionWidth { get; init; } = 2048;
    public int MaximumRegions { get; init; } = 128;
    public int MaximumTextCharacters { get; init; } = 16_384;
    public int MaximumIntermediateBytes { get; init; } = 128 * 1024 * 1024;
    public double DetectionThreshold { get; init; } = 0.3;
    public double BoxThreshold { get; init; } = 0.5;
    public double MinimumConfidence { get; init; } = 0.5;
}

public sealed record OcrPoint(double X, double Y);

/// <summary>Corners follow top-left, top-right, bottom-right, bottom-left in the EXIF-normalized image. Confidence is an uncalibrated CTC score.</summary>
public sealed record OcrRegion(string Text, double Confidence, IReadOnlyList<OcrPoint> Corners);

/// <summary>Regions are sorted approximately by vertical position, then horizontal position; this is not document reading-order analysis.</summary>
public sealed record OcrResult(int Width, int Height, IReadOnlyList<OcrRegion> Regions);
