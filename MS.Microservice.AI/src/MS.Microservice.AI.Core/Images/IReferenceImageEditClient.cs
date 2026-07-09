using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Narrow abstraction for reference-image editing within the educational
/// image generation subsystem. This is NOT a global AI gateway contract —
/// it lives in Core.Images to avoid expanding <c>MS.Microservice.AI.Abstractions</c>.
/// </summary>
public interface IReferenceImageEditClient
{
    /// <summary>
    /// Edits a reference image by URL with a text prompt and optional negative prompt.
    /// </summary>
    /// <param name="request">The reference edit request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The edited images.</returns>
    ValueTask<AIImageResponse> EditReferenceAsync(ReferenceImageEditRequest request, CancellationToken ct = default);
}
