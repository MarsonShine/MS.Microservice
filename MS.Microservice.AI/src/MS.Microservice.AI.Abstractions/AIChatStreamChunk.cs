namespace MS.Microservice.AI.Abstractions;

public sealed record AIChatStreamChunk
{
    public string DeltaText { get; init; } = string.Empty;

    public bool IsFinal { get; init; }

    public string? FinishReason { get; init; }

    public AIUsage? Usage { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? ProviderRequestId { get; init; }
}