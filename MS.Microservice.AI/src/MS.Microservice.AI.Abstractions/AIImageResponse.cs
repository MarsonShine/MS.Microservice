namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The result of an image generation or edit request.
/// </summary>
public sealed record AIImageResponse
{
    /// <summary>The provider that produced the images.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that produced the images.</summary>
    public required string Model { get; init; }

    /// <summary>The generated or edited images. At least one image is guaranteed when the request succeeds.</summary>
    public required IReadOnlyList<AIImageData> Images { get; init; }

    /// <summary>Token usage reported by the provider, or <see cref="AIUsage.Zero"/> if unavailable.</summary>
    public AIUsage Usage { get; init; } = AIUsage.Zero;

    /// <summary>Provider-assigned request identifier for support/tracing.</summary>
    public string? ProviderRequestId { get; init; }
}