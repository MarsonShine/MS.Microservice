namespace MS.Microservice.Documents.Pdf;

public sealed record PdfLimits
{
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
    public long MaximumMergedInputBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumOutputBytes { get; init; } = 128 * 1024 * 1024;
    public int MaximumPages { get; init; } = 1000;
    public int MaximumRasterDimension { get; init; } = 16_384;
    public long MaximumRasterPixels { get; init; } = 40_000_000;
}

public sealed class PdfOptions
{
    public string? Password { get; init; }
    public string? OutputUserPassword { get; init; }
    public string? OutputOwnerPassword { get; init; }
    public bool AllowUnencryptedOutput { get; init; }
    public PdfLimits Limits { get; init; } = new();
}

public sealed record PdfPageInfo(int PageNumber, double WidthPoints, double HeightPoints, int RotationDegrees);
public sealed record PdfDocumentInfo(string Title, string Author, bool IsEncrypted, IReadOnlyList<PdfPageInfo> Pages);
public sealed record PdfPart(int PageNumber, byte[] Content);
public sealed record PdfPageText(int PageNumber, string Text);
public sealed record PdfRenderedPage(int PageNumber, int Width, int Height, byte[] Png);
