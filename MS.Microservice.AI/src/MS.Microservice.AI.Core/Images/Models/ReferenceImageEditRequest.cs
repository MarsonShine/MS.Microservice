namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Request model for reference-image editing in the educational image generation pipeline.
/// Mirrors the Qwen-specific request but lives in Core.Images to avoid coupling
/// Core to any specific provider.
/// </summary>
public sealed record ReferenceImageEditRequest
{
    /// <summary>The text edit instruction. Must be non-empty.</summary>
    public required string Prompt { get; init; }

    /// <summary>Publicly accessible URL of the source image to edit.</summary>
    public required string ReferenceImageUrl { get; init; }

    /// <summary>Optional negative prompt.</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>Optional model override.</summary>
    public string? Model { get; init; }

    /// <summary>Scenario key to look up in configuration.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied correlation id for tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Number of images to produce.</summary>
    public int? Count { get; init; }

    /// <summary>Output dimensions, e.g. <c>1024x1024</c>.</summary>
    public string? Size { get; init; }

    /// <summary>Per-request timeout override.</summary>
    public TimeSpan? Timeout { get; init; }
}
