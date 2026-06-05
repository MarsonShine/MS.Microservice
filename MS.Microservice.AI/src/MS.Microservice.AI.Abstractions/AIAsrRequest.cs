namespace MS.Microservice.AI.Abstractions;

public sealed record AIAsrRequest
{
    public required AIBinaryContent Audio { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Scenario { get; init; }

    public string? RequestId { get; init; }

    public string? Language { get; init; }

    public string? Prompt { get; init; }

    public string? ResponseFormat { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}