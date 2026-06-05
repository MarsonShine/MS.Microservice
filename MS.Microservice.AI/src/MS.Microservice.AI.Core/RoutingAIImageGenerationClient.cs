using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIImageGenerationClient : IAIImageGenerationClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;

    public RoutingAIImageGenerationClient(IAIModelResolver modelResolver, IAIProviderFactory providerFactory)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
    }

    public async ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateImageGenerationRequest(request);
        var model = _modelResolver.ResolveImageGenerationModel(request);
        var provider = _providerFactory.GetRequiredImageGenerationProvider(model.Provider);
        return await provider.GenerateAsync(model, request, cancellationToken).ConfigureAwait(false);
    }
}