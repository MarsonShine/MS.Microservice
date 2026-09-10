using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class RoutingAIChatClient : IAIChatClient
{
    private readonly IAIModelResolver _modelResolver;
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptions<AIPayloadLimitOptions>? _payloadLimits;
    private readonly AIProductionPipeline? _pipeline;

    public RoutingAIChatClient(
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

    public async ValueTask<AIChatResponse> GetResponseAsync(AIChatRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateChatRequest(request, _payloadLimits?.Value);
        var model = _modelResolver.ResolveChatModel(request);
        var provider = _providerFactory.GetRequiredChatProvider(model.Provider);
        if (_pipeline is null)
        {
            return await provider.GetResponseAsync(model, request, cancellationToken).ConfigureAwait(false);
        }

        return await _pipeline.ExecuteAsync(
            CreateContext(model, request),
            token => provider.GetResponseAsync(model, request, token),
            response => response.Usage,
            cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIChatRequest request, CancellationToken cancellationToken = default)
    {
        AIRequestValidator.ValidateChatRequest(request, _payloadLimits?.Value, isStreaming: true);
        var model = _modelResolver.ResolveChatModel(request);
        var provider = _providerFactory.GetRequiredChatProvider(model.Provider);
        return _pipeline is null
            ? provider.StreamAsync(model, request, cancellationToken)
            : _pipeline.ExecuteStreamAsync(
                CreateContext(model, request),
                token => provider.StreamAsync(model, request, token),
                chunk => chunk.IsFinal ? chunk.Usage : null,
                cancellationToken);
    }

    private static AIRequestContext CreateContext(AIResolvedModel model, AIChatRequest request)
    {
        return new AIRequestContext
        {
            Provider = model.Provider,
            Model = model.Model,
            Capability = AICapability.Chat,
            Scenario = model.Scenario,
            RequestId = request.RequestId,
        };
    }
}
