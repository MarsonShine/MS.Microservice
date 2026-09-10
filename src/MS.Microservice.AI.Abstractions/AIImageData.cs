namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// A single image result returned by an image generation or edit request.
/// Either <see cref="Url"/> or <see cref="Content"/> will be populated depending
/// on the requested <c>response_format</c>.
/// </summary>
public sealed record AIImageData
{
    /// <summary>Publicly accessible URL of the generated/edited image (when <c>response_format</c> is <c>url</c>).</summary>
    public string? Url { get; init; }

    /// <summary>Base64-decoded image bytes (when <c>response_format</c> is <c>b64_json</c>).</summary>
    public AIBinaryContent? Content { get; init; }

    /// <summary>The revised prompt used by the model (DALL·E / image generation).</summary>
    public string? RevisedPrompt { get; init; }
}