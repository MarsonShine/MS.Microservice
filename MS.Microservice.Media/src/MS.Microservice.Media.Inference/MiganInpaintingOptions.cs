using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference;

public sealed record MiganInpaintingOptions
{
    public required string ModelPath { get; init; }
    public int CpuThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    public ImageLimits Limits { get; init; } = new()
    {
        MaximumDimension = 4096, MaximumPixels = 4_000_000, MaximumEncodedBytes = 32 * 1024 * 1024
    };

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CpuThreads);
        ImageCodec.ValidateLimits(Limits);
    }
}

/// <summary>Counts describe selected pixels and actual changes to normalized decoded RGBA pixels.</summary>
public sealed record InpaintingResult(int Width, int Height, int SelectedPixelCount, int EditedPixelCount, string ModelId);
