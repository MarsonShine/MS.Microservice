namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The result of a non-streaming chat / text completion request.
/// </summary>
public sealed record AIChatResponse
{
    /// <summary>The provider that produced this response.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that produced this response (may differ from the request if the provider aliases).</summary>
    public required string Model { get; init; }

    /// <summary>The full completion text.</summary>
    public required string Text { get; init; }

    /// <summary>The reason generation stopped, e.g. <c>stop</c>, <c>length</c>.</summary>
    public string? FinishReason { get; init; }

    /// <summary>Token usage reported by the provider, or <see cref="AIUsage.Zero"/> if unavailable.</summary>
    public AIUsage Usage { get; init; } = AIUsage.Zero;

    /// <summary>Provider-assigned request identifier for support/tracing.</summary>
    public string? ProviderRequestId { get; init; }

    /// <summary>Provider-specific response metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}