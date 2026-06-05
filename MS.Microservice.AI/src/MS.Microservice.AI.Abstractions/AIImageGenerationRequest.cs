namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents an image generation request from a text prompt.
/// </summary>
public sealed record AIImageGenerationRequest
{
    /// <summary>The text description of the desired image. Must be non-empty.</summary>
    public required string Prompt { get; init; }

    /// <summary>Optional provider override. Must be accompanied by <see cref="Model"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model override.</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in <c>AI:Models:ImageGeneration</c> configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Number of images to generate. Overrides the configured default.</summary>
    public int? Count { get; init; }

    /// <summary>Output dimensions, e.g. <c>1024x1024</c>.</summary>
    public string? Size { get; init; }

    /// <summary>Quality setting, e.g. <c>standard</c> or <c>hd</c>.</summary>
    public string? Quality { get; init; }

    /// <summary>Response format, e.g. <c>url</c> or <c>b64_json</c>.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}