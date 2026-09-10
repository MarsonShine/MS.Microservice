using MS.Microservice.AI.Qwen;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.Samples.EducationalImages;
using MS.Microservice.Samples.EducationalImages.Models;

namespace MS.Microservice.Samples.EducationalImages.Qwen;

/// <summary>
/// Adapts <see cref="IQwenImageReferenceEditClient"/> to the education scenario
/// <see cref="IReferenceImageEditClient"/> contract, keeping Qwen-specific
/// types outside the scenario contracts.
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
