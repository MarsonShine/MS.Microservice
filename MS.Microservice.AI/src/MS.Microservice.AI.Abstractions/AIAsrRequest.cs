namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents an automatic speech recognition / transcription request.
/// </summary>
public sealed record AIAsrRequest
{
    /// <summary>The audio file to transcribe. Must be non-empty.</summary>
    public required AIBinaryContent Audio { get; init; }

    /// <summary>Optional provider override. Must be accompanied by <see cref="Model"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model override (e.g. <c>whisper-1</c>).</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in <c>AI:Models:Asr</c> configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Input language hint in ISO-639-1 format (e.g. <c>zh</c>, <c>en</c>).</summary>
    public string? Language { get; init; }

    /// <summary>Optional guiding prompt to improve transcription accuracy.</summary>
    public string? Prompt { get; init; }

    /// <summary>Response format, e.g. <c>json</c>, <c>text</c>, <c>verbose_json</c>.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}