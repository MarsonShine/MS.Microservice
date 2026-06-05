using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAITtsClient : IAITtsClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;

    public RoutingAITtsClient(IAIModelResolver modelResolver, IAIProviderFactory providerFactory)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
    }

    public async ValueTask<AITtsResponse> SynthesizeAsync(AITtsRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateTtsRequest(request);
        var model = _modelResolver.ResolveTtsModel(request);
        var provider = _providerFactory.GetRequiredTtsProvider(model.Provider);
        return await provider.SynthesizeAsync(model, request, cancellationToken).ConfigureAwait(false);
    }
}