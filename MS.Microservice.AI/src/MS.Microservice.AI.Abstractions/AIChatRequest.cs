namespace MS.Microservice.AI.Abstractions;

public sealed record AIChatRequest
{
    public required IReadOnlyList<AIChatMessage> Messages { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Scenario { get; init; }

    public string? RequestId { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? MaxOutputTokens { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}