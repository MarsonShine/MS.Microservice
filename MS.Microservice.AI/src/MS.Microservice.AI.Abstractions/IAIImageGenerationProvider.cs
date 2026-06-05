namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Contract for a concrete image generation provider implementation.
/// </summary>
public interface IAIImageGenerationProvider
{
    /// <summary>The unique provider name.</summary>
    string Name { get; }

    /// <summary>
    /// Generates images from a text prompt.
    /// </summary>
    /// <param name="model">Fully resolved model configuration.</param>
    /// <param name="request">The original generation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated images.</returns>
    ValueTask<AIImageResponse> GenerateAsync(AIResolvedModel model, AIImageGenerationRequest request, CancellationToken cancellationToken = default);
}