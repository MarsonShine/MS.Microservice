using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Qwen;

/// <summary>
/// Adapts <see cref="IQwenImageReferenceEditClient"/> to the Core.Images
/// <see cref="IReferenceImageEditClient"/> contract, keeping Qwen-specific
/// types out of the Core project.
/// </summary>
public sealed class QwenReferenceImageEditAdapter(IQwenImageReferenceEditClient client) : IReferenceImageEditClient
{
    private readonly IQwenImageReferenceEditClient client = client;

    public async ValueTask<AIImageResponse> EditReferenceAsync(ReferenceImageEditRequest request, CancellationToken ct = default)
    {
        var qwenRequest = new QwenImageReferenceEditRequest
        {
            Prompt = request.Prompt,
            ReferenceImageUrl = request.ReferenceImageUrl,
            NegativePrompt = request.NegativePrompt,
            Model = request.Model,
            Scenario = request.Scenario,
            RequestId = request.RequestId,
            Count = request.Count,
            Size = request.Size,
            Timeout = request.Timeout,
        };

        return await client.EditReferenceAsync(qwenRequest, ct).ConfigureAwait(false);
    }
}
