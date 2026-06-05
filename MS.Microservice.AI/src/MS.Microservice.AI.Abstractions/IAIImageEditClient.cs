namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Provider-neutral entry point for image editing (inpainting, background removal, etc.).
/// </summary>
public interface IAIImageEditClient
{
    /// <summary>
    /// Edits an existing image according to a text prompt, optionally using a mask.
    /// </summary>
    /// <param name="request">The edit request. <see cref="AIImageEditRequest.Prompt"/> and <see cref="AIImageEditRequest.Image"/> must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The edited images.</returns>
    /// <exception cref="AIConfigurationException">Configuration is missing or invalid.</exception>
    /// <exception cref="AIProviderException">The provider returned an error.</exception>
    ValueTask<AIImageResponse> EditAsync(AIImageEditRequest request, CancellationToken cancellationToken = default);
}