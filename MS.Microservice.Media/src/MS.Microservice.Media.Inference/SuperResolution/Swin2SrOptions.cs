using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Inference.SuperResolution;

public sealed record Swin2SrOptions
{
    public required string ModelPath { get; init; }
    public int CpuThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    public ImageLimits Limits { get; init; } = new() { MaximumDimension = 4096, MaximumPixels = 4_000_000, MaximumEncodedBytes = 32 * 1024 * 1024 };
    public int MaximumOutputDimension { get; init; } = 8192;
    public int MaximumOutputPixels { get; init; } = 16_000_000;
    public int TileSize { get; init; } = 128;
    public int ContextPixels { get; init; } = 16;
}

/// <summary>Dimensions describe the EXIF-normalized image and its twofold PNG output. ReviewRecommended is always true.</summary>
public sealed record SuperResolutionResult(int InputWidth, int InputHeight, int Width, int Height, int Tiles, string ModelId)
{
    public bool ReviewRecommended => true;
}
