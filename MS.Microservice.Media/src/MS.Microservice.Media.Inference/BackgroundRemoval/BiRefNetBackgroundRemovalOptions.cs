using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference.BackgroundRemoval;

public sealed record BiRefNetBackgroundRemovalOptions
{
    public required string ModelPath { get; init; }
    public int CpuThreads { get; init; } = Math.Min(2, Environment.ProcessorCount);
    public ImageLimits Limits { get; init; } = new() { MaximumDimension = 4096, MaximumPixels = 4_000_000, MaximumEncodedBytes = 32 * 1024 * 1024 };

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelPath);
        ImageCodec.ValidateLimits(Limits);
        if (CpuThreads is < 1 or > 64) throw new ArgumentException("Use 1–64 CPU threads per session.");
    }
}

/// <summary>Dimensions follow EXIF normalization; alpha statistics describe the output matte, not segmentation accuracy.</summary>
public sealed record BackgroundRemovalResult(int Width, int Height, int NonTransparentPixels, double MeanAlpha, string ModelId)
{
    public bool ReviewRecommended => true;
}
