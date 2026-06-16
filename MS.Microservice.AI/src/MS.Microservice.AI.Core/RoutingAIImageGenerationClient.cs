using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIImageGenerationClient : IAIImageGenerationClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptions<AIPayloadLimitOptions>? _payloadLimits;
    private readonly AIProductionPipeline? _pipeline;

    public RoutingAIImageGenerationClient(
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

    public async ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateImageGenerationRequest(request, _payloadLimits?.Value);
        var model = _modelResolver.ResolveImageGenerationModel(request);
        var provider = _providerFactory.GetRequiredImageGenerationProvider(model.Provider);
        if (_pipeline is null)
        {
            return await provider.GenerateAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        return await _pipeline.ExecuteAsync(
            CreateContext(model, request),
            token => provider.GenerateAsync(model, request, token),
            response => response.Usage,
            cancellationToken).ConfigureAwait(false);
    }

    private static AIRequestContext CreateContext(AIResolvedModel model, AIImageGenerationRequest request)
    {
        return new AIRequestContext
        {
            Provider = model.Provider,
            Model = model.Model,
            Capability = AICapability.ImageGeneration,
            Scenario = model.Scenario,
            RequestId = request.RequestId,
        };
    }
}
