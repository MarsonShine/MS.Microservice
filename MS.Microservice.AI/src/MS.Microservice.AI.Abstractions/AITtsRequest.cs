namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents a text-to-speech synthesis request.
/// </summary>
public sealed record AITtsRequest
{
    /// <summary>The text to convert to speech. Must be non-empty.</summary>
    public required string Input { get; init; }

    /// <summary>Optional provider override. Must be accompanied by <see cref="Model"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model override.</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in <c>AI:Models:Tts</c> configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Voice identifier (e.g. <c>alloy</c>, <c>chelsie</c>). Falls back to the resolved model configuration.</summary>
    public string? Voice { get; init; }

    /// <summary>Audio format override, e.g. <c>mp3</c>, <c>wav</c>, <c>opus</c>.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Speech speed multiplier. Values greater than 1.0 produce faster speech.</summary>
    public double? Speed { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}