using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAITtsClient : IAITtsClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptions<AIPayloadLimitOptions>? _payloadLimits;
    private readonly AIProductionPipeline? _pipeline;

    public RoutingAITtsClient(
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

    public async ValueTask<AITtsResponse> SynthesizeAsync(AITtsRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateTtsRequest(request, _payloadLimits?.Value);
        var model = _modelResolver.ResolveTtsModel(request);
        var provider = _providerFactory.GetRequiredTtsProvider(model.Provider);
        if (_pipeline is null)
        {
            return await provider.SynthesizeAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        return await _pipeline.ExecuteAsync(
            CreateContext(model, request),
            token => provider.SynthesizeAsync(model, request, token),
            _ => AIUsage.Zero,
            cancellationToken).ConfigureAwait(false);
    }

    private static AIRequestContext CreateContext(AIResolvedModel model, AITtsRequest request)
    {
        return new AIRequestContext
        {
            Provider = model.Provider,
            Model = model.Model,
            Capability = AICapability.Tts,
            Scenario = model.Scenario,
            RequestId = request.RequestId,
        };
    }
}
