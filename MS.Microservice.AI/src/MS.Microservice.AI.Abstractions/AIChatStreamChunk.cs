namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// A single chunk yielded during a streaming chat completion.
/// The final chunk has <see cref="IsFinal"/> set to <c>true</c> and
/// may carry the aggregated <see cref="Usage"/>.
/// </summary>
public sealed record AIChatStreamChunk
{
    /// <summary>Incremental text produced in this chunk.</summary>
    public string DeltaText { get; init; } = string.Empty;

    /// <summary>When <c>true</c>, this is the terminal chunk of the stream.</summary>
    public bool IsFinal { get; init; }

    /// <summary>The reason generation stopped (only meaningful on the final chunk).</summary>
    public string? FinishReason { get; init; }

    /// <summary>Aggregated token usage, typically present only on the final chunk or a trailing usage envelope.</summary>
    public AIUsage? Usage { get; init; }

    /// <summary>The provider that produced this chunk.</summary>
    public string? Provider { get; init; }

    /// <summary>The resolved model name.</summary>
    public string? Model { get; init; }

    /// <summary>Provider-assigned request identifier for support/tracing.</summary>
    public string? ProviderRequestId { get; init; }
}