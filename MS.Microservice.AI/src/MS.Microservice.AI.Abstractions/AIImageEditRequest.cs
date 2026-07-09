namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents an image edit (inpainting / background removal) request.
/// Either <see cref="Image"/> or <see cref="ReferenceImageUrl"/> must be provided, but not both.
/// </summary>
public sealed record AIImageEditRequest
{
    /// <summary>The text description of the desired edit. Must be non-empty.</summary>
    public required string Prompt { get; init; }

    /// <summary>The source image bytes to edit. Mutually exclusive with <see cref="ReferenceImageUrl"/>.</summary>
    public AIBinaryContent? Image { get; init; }

    /// <summary>
    /// A publicly accessible URL of the source image to edit (used for reference-based editing).
    /// Mutually exclusive with <see cref="Image"/>. Supports <c>http</c>, <c>https</c>, and <c>oss</c> schemes.
    /// </summary>
    public string? ReferenceImageUrl { get; init; }

    /// <summary>Optional negative prompt for providers that support it (e.g. Qwen multimodal edit).</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>Optional mask image indicating which areas to edit. Transparent areas are edited.
    /// Can only be used together with <see cref="Image"/>, not with <see cref="ReferenceImageUrl"/>.</summary>
    public AIBinaryContent? Mask { get; init; }

    /// <summary>Optional provider override. Must be accompanied by <see cref="Model"/>.</summary>
    public string? Provider { get; init; }

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

    /// <summary>Quality setting, e.g. <c>standard</c> or <c>hd</c>.</summary>
    public string? Quality { get; init; }

    /// <summary>Response format, e.g. <c>url</c> or <c>b64_json</c>.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Arbitrary key/value pairs forwarded to provider-specific extensions.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}