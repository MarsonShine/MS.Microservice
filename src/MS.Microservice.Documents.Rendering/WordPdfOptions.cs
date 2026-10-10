namespace MS.Microservice.Documents.Rendering;

/// <summary>Trusted host paths for an installed LibreOffice runtime; these must not come from submitted documents.</summary>
public sealed record WordPdfHostOptions
{
    public required string LibreOfficeExecutablePath { get; init; }
    public string DotNetHostPath { get; init; } = "dotnet";
    public string TemporaryDirectory { get; init; } = Path.GetTempPath();
}

public sealed record WordPdfLimits
{
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 1024;
    public long MaximumExpandedBytes { get; init; } = 128L * 1024 * 1024;
    public long MaximumXmlCharactersPerPart { get; init; } = 16_000_000;
    public long MaximumImagePixels { get; init; } = 20_000_000;
    public int MaximumPdfBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumPages { get; init; } = 100;
}

public sealed record WordPdfOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public WordPdfLimits Limits { get; init; } = new();
}

public sealed record WordPdfResult(int Pages, int Bytes);

internal sealed record WordWorkerRequest(byte[] Docx, WordPdfHostOptions Office, WordPdfOptions Options);
