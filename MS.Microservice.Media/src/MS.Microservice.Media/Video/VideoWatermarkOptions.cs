using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Video;

public sealed record VideoWatermarkOptions
{
    public WatermarkPlacement Placement { get; init; } = new();
    public TimeSpan Start { get; init; }
    public TimeSpan? End { get; init; }
    public int ConstantRateFactor { get; init; } = 23;
    public VideoEncodingSpeed Speed { get; init; } = VideoEncodingSpeed.Fast;
}
