namespace MS.Microservice.Documents.Word;

public enum MissingWordTemplateValue { Throw, KeepOriginal }

public sealed record WordTemplateLimits
{
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumOutputBytes { get; init; } = 128 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 128L * 1024 * 1024;
    public long MaximumXmlCharactersPerPart { get; init; } = 16_000_000;
    public int MaximumEntries { get; init; } = 1024;
    public long MaximumReplacementCharacters { get; init; } = 16_000_000;
}

public sealed record WordTemplateOptions
{
    public MissingWordTemplateValue MissingValue { get; init; } = MissingWordTemplateValue.Throw;
    public WordTemplateLimits Limits { get; init; } = new();
}

public sealed record WordTemplateResult(int FilledControls);
