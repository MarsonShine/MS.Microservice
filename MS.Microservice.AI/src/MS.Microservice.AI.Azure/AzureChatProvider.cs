using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureChatProvider(
    AzureDeploymentRouter deployments,
    AzureChatCompletionsAdapter chatCompletions,
    AzureResponsesChatAdapter responses) : IAIChatProvider
{
    public string Name => AzureOptions.ProviderName;

    public ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var target = deployments.ResolveDeployment(model);
        return target.Deployment.Api is AzureDeploymentApi.Responses
            ? responses.GetResponseAsync(target.CreateRequestTarget("responses"), model, request, cancellationToken)
            : chatCompletions.GetResponseAsync(model, request, cancellationToken);
    }

    public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var target = deployments.ResolveDeployment(model);
        return target.Deployment.Api is AzureDeploymentApi.Responses
            ? responses.StreamAsync(target.CreateRequestTarget("responses"), model, request, cancellationToken)
            : chatCompletions.StreamAsync(model, request, cancellationToken);
    }
}

internal sealed class AzureChatCompletionsAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureChatCompletionsAdapter> logger,
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
        if (payload.MaxTokens is null || deployments.ResolveDeployment(model).Deployment.MaxTokenParameter == "max_tokens")
        {
            return;
        }

        payload.MaxCompletionTokens = payload.MaxTokens;
        payload.MaxTokens = null;
    }
}
