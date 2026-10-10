using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed record VideoConcatenationOptions
{
    public ImageSize? Canvas { get; init; }
    public double? FrameRate { get; init; }
    public int MaximumInputs { get; init; } = 32;
    public int ConstantRateFactor { get; init; } = 23;
    public VideoEncodingSpeed Speed { get; init; } = VideoEncodingSpeed.Fast;
    public int MaximumDimension { get; init; } = 16_384;
    public long MaximumPixels { get; init; } = 40_000_000;
}
