namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Provider-neutral entry point for image generation from a text prompt.
/// </summary>
public interface IAIImageGenerationClient
{
    /// <summary>
    /// Generates one or more images from a text description.
    /// </summary>
    /// <param name="request">The generation request. <see cref="AIImageGenerationRequest.Prompt"/> must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated images (URLs or base64 content depending on format).</returns>
    /// <exception cref="AIConfigurationException">Configuration is missing or invalid.</exception>
    /// <exception cref="AIProviderException">The provider returned an error.</exception>
    ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest request, CancellationToken cancellationToken = default);
}