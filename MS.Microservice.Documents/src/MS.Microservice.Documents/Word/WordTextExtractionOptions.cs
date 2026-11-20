namespace MS.Microservice.Documents.Word;

/// <summary>Bounds read-only DOCX extraction; byte budgets do not represent a process memory quota.</summary>
public sealed record WordTextExtractionOptions
{
    public bool IncludeHeadersAndFooters { get; init; } = true;
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 1024;
    public int MaximumExpandedXmlBytes { get; init; } = 128 * 1024 * 1024;
    public long MaximumXmlCharactersPerPart { get; init; } = 16_000_000;
    public int MaximumTextCharacters { get; init; } = 16_000_000;
}
