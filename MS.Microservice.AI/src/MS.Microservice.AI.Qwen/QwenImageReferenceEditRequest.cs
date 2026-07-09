namespace MS.Microservice.AI.Qwen;

/// <summary>
/// Qwen-specific reference-image edit request. Uses a publicly accessible
/// <see cref="ReferenceImageUrl"/> instead of binary image data.
/// </summary>
public sealed record QwenImageReferenceEditRequest
{
    /// <summary>The text edit instruction. Must be non-empty.</summary>
    public required string Prompt { get; init; }

    /// <summary>Publicly accessible URL of the source image to edit.</summary>
    public required string ReferenceImageUrl { get; init; }

    /// <summary>Optional negative prompt (e.g. "style transfer, full redraw").</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>Optional model override.</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in <c>AI:Models:ImageEdit</c> configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Number of images to produce.</summary>
    public int? Count { get; init; }

    /// <summary>Output dimensions, e.g. <c>1024x1024</c>.</summary>
    public string? Size { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
