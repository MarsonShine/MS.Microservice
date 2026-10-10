using MS.Microservice.Media.Images;

namespace MS.Microservice.Media.Codes;

public enum CodeFormat { Qr, Code128, Ean13, Ean8, UpcA, Code39, DataMatrix, Pdf417 }
public enum QrErrorCorrection { Low, Medium, Quartile, High }
public sealed record CodePoint(float X, float Y);
public sealed record DecodedCode(string Text, CodeFormat Format, IReadOnlyList<CodePoint> Points);

public sealed record CodeGenerationOptions
{
    public CodeFormat Format { get; init; } = CodeFormat.Qr;
    public int Width { get; init; } = 256;
    public int Height { get; init; } = 256;
    /// <summary>Format-specific margin; null uses one module for DataMatrix and other formats' engine defaults.</summary>
    public int? Margin { get; init; }
    public QrErrorCorrection QrErrorCorrection { get; init; } = QrErrorCorrection.Medium;
    public ImageColor Foreground { get; init; } = new(0, 0, 0);
    public ImageColor Background { get; init; } = new(255, 255, 255);
    public int MaximumTextCharacters { get; init; } = 4096;
    public ImageLimits Limits { get; init; } = new();
}

public sealed record CodeReadingOptions
{
    public IReadOnlyCollection<CodeFormat>? Formats { get; init; }
    public bool AutoRotate { get; init; } = true;
    public bool TryHarder { get; init; } = true;
    public bool TryInverted { get; init; } = true;
    public ImageLimits Limits { get; init; } = new();
}
