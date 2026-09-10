using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIImageEditClient : IAIImageEditClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptions<AIPayloadLimitOptions>? _payloadLimits;
    private readonly AIProductionPipeline? _pipeline;

    public RoutingAIImageEditClient(
        IAIModelResolver modelResolver,
        IAIProviderFactory providerFactory,
        IOptions<AIPayloadLimitOptions>? payloadLimits = null,
        AIProductionPipeline? pipeline = null)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
        _payloadLimits = payloadLimits;
        _pipeline = pipeline;
    }

    public async ValueTask<AIImageResponse> EditAsync(AIImageEditRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateImageEditRequest(request, _payloadLimits?.Value);
        var model = _modelResolver.ResolveImageEditModel(request);
        var provider = _providerFactory.GetRequiredImageEditProvider(model.Provider);
        if (_pipeline is null)
        {
            return await provider.EditAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        return await _pipeline.ExecuteAsync(
            CreateContext(model, request),
            token => provider.EditAsync(model, request, token),
            response => response.Usage,
            cancellationToken).ConfigureAwait(false);
    }

    private static AIRequestContext CreateContext(AIResolvedModel model, AIImageEditRequest request)
    {
        return new AIRequestContext
        {
            Provider = model.Provider,
            Model = model.Model,
            Capability = AICapability.ImageEdit,
            Scenario = model.Scenario,
            RequestId = request.RequestId,
        };
    }
}
