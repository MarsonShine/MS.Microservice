using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

/// <summary>
/// Routing client for Qwen reference-image editing. Resolves the model via
/// <see cref="IAIModelResolver"/> and delegates to <see cref="QwenImageEditProvider"/>.
/// </summary>
public sealed class QwenImageReferenceEditClient(
    IAIModelResolver modelResolver,
    IEnumerable<IAIImageEditProvider> providers,
    IOptions<AIPayloadLimitOptions>? payloadLimits = null) : IQwenImageReferenceEditClient
{
    private readonly IAIModelResolver modelResolver = modelResolver;
    private readonly QwenImageEditProvider provider = (QwenImageEditProvider)providers.First(p => p.Name == QwenProviderDefaults.ProviderName);
    private readonly IOptions<AIPayloadLimitOptions>? payloadLimits = payloadLimits;

    public async ValueTask<AIImageResponse> EditReferenceAsync(
        QwenImageReferenceEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new AIConfigurationException("Qwen image reference edit request must include a prompt.");

        if (string.IsNullOrWhiteSpace(request.ReferenceImageUrl))
            throw new AIConfigurationException("Qwen image reference edit request must include a ReferenceImageUrl.");

        // Resolve model via the standard AI model resolver
        var resolvedModel = modelResolver.ResolveImageEditModel(new AIImageEditRequest
        {
            Prompt = request.Prompt,
            Image = new AIBinaryContent { Content = [0] }, // dummy; resolution only reads Provider/Model/Scenario
            Provider = null,
            Model = request.Model,
            Scenario = request.Scenario,
            RequestId = request.RequestId,
            Count = request.Count,
            Size = request.Size,
            Timeout = request.Timeout,
        });

        return await provider.EditReferenceAsync(resolvedModel, request, cancellationToken).ConfigureAwait(false);
    }
}
