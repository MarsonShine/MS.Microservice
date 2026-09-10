using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Qwen;

/// <summary>
/// Qwen-specific client for reference-image editing via the multimodal generation API.
/// </summary>
public interface IQwenImageReferenceEditClient
{
    /// <summary>
    /// Edits a reference image by URL using the Qwen multimodal generation endpoint.
    /// </summary>
    /// <param name="request">The edit request with a reference image URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The edited images.</returns>
    ValueTask<AIImageResponse> EditReferenceAsync(QwenImageReferenceEditRequest request, CancellationToken cancellationToken = default);
}
