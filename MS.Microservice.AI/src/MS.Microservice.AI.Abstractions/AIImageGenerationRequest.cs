namespace MS.Microservice.AI.Abstractions;

public sealed record AIImageGenerationRequest
{
    public required string Prompt { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Scenario { get; init; }

    public string? RequestId { get; init; }

    public int? Count { get; init; }

    public string? Size { get; init; }

    public string? Quality { get; init; }

    public string? ResponseFormat { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}