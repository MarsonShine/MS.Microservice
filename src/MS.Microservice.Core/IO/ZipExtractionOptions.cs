namespace MS.Microservice.Core.IO;

/// <summary>Limits applied to actual decompressed ZIP content.</summary>
public sealed record ZipExtractionOptions
{
    public int MaxEntries { get; init; } = 10_000;

    public long MaxEntryBytes { get; init; } = 512L * 1024 * 1024;

    public long MaxTotalBytes { get; init; } = 1024L * 1024 * 1024;
}
