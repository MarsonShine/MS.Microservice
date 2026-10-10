using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public enum VideoEncodingSpeed { Fast, Balanced, Slow }

public sealed record VideoEditOptions
{
    public TimeSpan Start { get; init; }
    public TimeSpan? Duration { get; init; }
    public ImageCrop? Crop { get; init; }
    public ImageSize? Resize { get; init; }
    public ImageResizeMode ResizeMode { get; init; } = ImageResizeMode.Contain;
    public int RotationDegrees { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public int ConstantRateFactor { get; init; } = 23;
    public VideoEncodingSpeed Speed { get; init; } = VideoEncodingSpeed.Fast;
    public int MaximumDimension { get; init; } = 16_384;
    public long MaximumPixels { get; init; } = 40_000_000;
}
