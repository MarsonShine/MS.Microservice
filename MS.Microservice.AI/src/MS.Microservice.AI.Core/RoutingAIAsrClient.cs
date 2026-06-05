using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIAsrClient : IAIAsrClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;

    public RoutingAIAsrClient(IAIModelResolver modelResolver, IAIProviderFactory providerFactory)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
    }

    public async ValueTask<AIAsrResponse> RecognizeAsync(AIAsrRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateAsrRequest(request);
        var model = _modelResolver.ResolveAsrModel(request);
        var provider = _providerFactory.GetRequiredAsrProvider(model.Provider);
        return await provider.RecognizeAsync(model, request, cancellationToken).ConfigureAwait(false);
    }
}