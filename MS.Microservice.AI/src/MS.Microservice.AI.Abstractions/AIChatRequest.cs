namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents a chat / text completion request. All provider and model selection
/// is optional — when omitted, the scenario-specific or default configuration is used.
/// </summary>
public sealed record AIChatRequest
{
    /// <summary>The conversation history. Must contain at least one message.</summary>
    public required IReadOnlyList<AIChatMessage> Messages { get; init; }

    /// <summary>Optional provider override (e.g. "OpenAI"). Must be accompanied by <see cref="Model"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model override (e.g. "gpt-4.1-mini").</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in <c>AI:Models:Chat</c> configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Override the configured sampling temperature.</summary>
    public double? Temperature { get; init; }

    /// <summary>Override the configured nucleus sampling parameter.</summary>
    public double? TopP { get; init; }

    /// <summary>Override the configured maximum output tokens.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Per-request timeout override. Falls back to the resolved model timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}