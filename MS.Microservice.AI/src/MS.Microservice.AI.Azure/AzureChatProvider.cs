using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureChatProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureChatProvider> logger,
    AzureDeploymentRouter deployments)
    : OpenAICompatibleChatProviderBase(httpClientFactory, options, concurrency, timeProvider, logger)
{
    public override string Name => AzureOptions.ProviderName;
    protected override string HttpClientName => AzureOptions.HttpClientName;
    protected override string DefaultBaseAddress => throw new InvalidOperationException("Azure requires a configured deployment endpoint.");

    protected override OpenAICompatibleRequestTarget ResolveTarget(AIResolvedModel model, string relativePath)
        => deployments.Resolve(model, relativePath);

    protected override void CustomizeRequest(OpenAICompatibleChatCompletionRequest payload, AIChatRequest request, AIResolvedModel model)
    {
        payload.MaxCompletionTokens = payload.MaxTokens;
        payload.MaxTokens = null;
    }
}
