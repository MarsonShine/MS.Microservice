namespace MS.Microservice.AI.Abstractions;

public sealed record AIChatResponse
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Text { get; init; }

    public string? FinishReason { get; init; }

    public AIUsage Usage { get; init; } = AIUsage.Zero;

    public string? ProviderRequestId { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}