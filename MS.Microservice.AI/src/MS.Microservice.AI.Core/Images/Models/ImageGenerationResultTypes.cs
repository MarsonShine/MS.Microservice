using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Overrides for reference-image edit generation that mirror
/// <see cref="AIImageEditRequest"/> optional fields.
/// </summary>
public sealed record ImageEditGenerationOverrides
{
    /// <summary>Optional provider override.</summary>
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

    /// <summary>Quality setting.</summary>
    public string? Quality { get; init; }

    /// <summary>Response format.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// The result of a single reference-image edit operation.
/// </summary>
public sealed record ReferenceImageEditResult(
    string? RichPrompt,
    string? SafePrompt,
    AIImageResponse ImageResponse,
    bool ReusedSourceImage,
    string ReferenceImageUrl);

/// <summary>
/// The result of generating (or editing) a single sentence image in a batch.
/// </summary>
public sealed record SentenceImageBatchGenerationResult
{
    public long RowId { get; init; }
    public string? SceneGroupId { get; init; }
    public string? RichPrompt { get; init; }
    public string? SafePrompt { get; init; }
    public required AIImageResponse ImageResponse { get; init; }
    public bool UsedReferenceEdit { get; init; }
    public bool ReusedSourceImage { get; init; }
    public string? ReferenceImageUrl { get; init; }
    public double ContextConfidence { get; init; }
}
