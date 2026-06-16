using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIAsrClient : IAIAsrClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptions<AIPayloadLimitOptions>? _payloadLimits;
    private readonly AIProductionPipeline? _pipeline;

    public RoutingAIAsrClient(
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

    public async ValueTask<AIAsrResponse> RecognizeAsync(AIAsrRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateAsrRequest(request, _payloadLimits?.Value);
        var model = _modelResolver.ResolveAsrModel(request);
        var provider = _providerFactory.GetRequiredAsrProvider(model.Provider);
        if (_pipeline is null)
        {
            return await provider.RecognizeAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        return await _pipeline.ExecuteAsync(
            CreateContext(model, request),
            token => provider.RecognizeAsync(model, request, token),
            response => response.Usage,
            cancellationToken).ConfigureAwait(false);
    }

    private static AIRequestContext CreateContext(AIResolvedModel model, AIAsrRequest request)
    {
        return new AIRequestContext
        {
            Provider = model.Provider,
            Model = model.Model,
            Capability = AICapability.Asr,
            Scenario = model.Scenario,
            RequestId = request.RequestId,
        };
    }
}
