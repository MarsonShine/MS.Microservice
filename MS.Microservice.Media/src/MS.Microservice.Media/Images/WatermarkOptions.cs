namespace MS.Microservice.Media.Images;

public enum WatermarkAnchor
{
    TopLeft, TopCenter, TopRight,
    CenterLeft, Center, CenterRight,
    BottomLeft, BottomCenter, BottomRight
}

public record WatermarkPlacement
{
    public WatermarkAnchor Anchor { get; init; } = WatermarkAnchor.BottomRight;
    public int MarginX { get; init; } = 16;
    public int MarginY { get; init; } = 16;
    public double Opacity { get; init; } = 0.35;
    public float RotationDegrees { get; init; }
    public bool Tile { get; init; }
    public int TileGap { get; init; } = 32;
    public int MaximumTiles { get; init; } = 10_000;
    public ImageLimits Limits { get; init; } = new();
}

public sealed record WatermarkOptions : WatermarkPlacement
{
    public ImageOutputFormat Format { get; init; } = ImageOutputFormat.Png;
    public int Quality { get; init; } = 90;
    public ImageColor JpegBackground { get; init; } = new(255, 255, 255);
}

public sealed record TextWatermark(string Text)
{
    public string? FontFile { get; init; }
    public float FontSize { get; init; } = 32;
    public ImageColor Color { get; init; } = new(255, 255, 255);
}
