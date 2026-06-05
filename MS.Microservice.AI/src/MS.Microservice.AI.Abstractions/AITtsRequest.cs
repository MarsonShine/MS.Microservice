namespace MS.Microservice.AI.Abstractions;

public sealed record AITtsRequest
{
    public required string Input { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Scenario { get; init; }

    public string? RequestId { get; init; }

    public string? Voice { get; init; }

    public string? ResponseFormat { get; init; }

    public double? Speed { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}