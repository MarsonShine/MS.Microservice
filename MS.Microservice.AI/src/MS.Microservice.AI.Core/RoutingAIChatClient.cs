using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIChatClient : IAIChatClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;

    public RoutingAIChatClient(IAIModelResolver modelResolver, IAIProviderFactory providerFactory)
    {
        _modelResolver = modelResolver;
        _providerFactory = providerFactory;
    }

    public async ValueTask<AIChatResponse> GetResponseAsync(AIChatRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateChatRequest(request);
        var model = _modelResolver.ResolveChatModel(request);
        var provider = _providerFactory.GetRequiredChatProvider(model.Provider);
        return await provider.GetResponseAsync(model, request, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIChatRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateChatRequest(request);
        var model = _modelResolver.ResolveChatModel(request);
        var provider = _providerFactory.GetRequiredChatProvider(model.Provider);
        return provider.StreamAsync(model, request, cancellationToken);
    }
}