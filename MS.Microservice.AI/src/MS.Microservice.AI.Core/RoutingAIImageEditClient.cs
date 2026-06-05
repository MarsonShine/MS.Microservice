using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIImageEditClient : IAIImageEditClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;

    public RoutingAIImageEditClient(IAIModelResolver modelResolver, IAIProviderFactory providerFactory)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
    }

    public async ValueTask<AIImageResponse> EditAsync(AIImageEditRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateImageEditRequest(request);
        var model = _modelResolver.ResolveImageEditModel(request);
        var provider = _providerFactory.GetRequiredImageEditProvider(model.Provider);
        return await provider.EditAsync(model, request, cancellationToken).ConfigureAwait(false);
    }
}