namespace MS.Microservice.Media.Images;

public enum ImageOutputFormat { Png, Jpeg, Webp }
public enum ImageResizeMode { Contain, Cover, Stretch }
public sealed record ImageCrop(int X, int Y, int Width, int Height);
public sealed record ImageSize(int Width, int Height);
public sealed record ImageColor(byte Red, byte Green, byte Blue, byte Alpha = 255);
public sealed record ImageProcessingResult(int Width, int Height, ImageOutputFormat Format);

public sealed record ImageLimits
{
    public int MaximumDimension { get; init; } = 16_384;
    public long MaximumPixels { get; init; } = 40_000_000;
    public int MaximumEncodedBytes { get; init; } = 64 * 1024 * 1024;
}

public sealed record ImageProcessingOptions
{
    public ImageOutputFormat Format { get; init; } = ImageOutputFormat.Png;
    public int Quality { get; init; } = 90;
    public ImageCrop? Crop { get; init; }
    public ImageSize? Resize { get; init; }
    public ImageResizeMode ResizeMode { get; init; } = ImageResizeMode.Contain;
    public int RotationDegrees { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public ImageColor JpegBackground { get; init; } = new(255, 255, 255);
    public ImageLimits Limits { get; init; } = new();
}
