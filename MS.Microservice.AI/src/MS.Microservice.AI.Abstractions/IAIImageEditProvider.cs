namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Contract for a concrete image edit provider implementation.
/// </summary>
public interface IAIImageEditProvider
{
    /// <summary>The unique provider name.</summary>
    string Name { get; }

    /// <summary>
    /// Edits an existing image according to a prompt and optional mask.
    /// </summary>
    /// <param name="model">Fully resolved model configuration.</param>
    /// <param name="request">The original edit request with image and optional mask.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The edited images.</returns>
    ValueTask<AIImageResponse> EditAsync(AIResolvedModel model, AIImageEditRequest request, CancellationToken cancellationToken = default);
}