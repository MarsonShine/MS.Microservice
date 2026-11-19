using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureTtsProvider> logger,
    AzureDeploymentRouter deployments)
    : OpenAICompatibleTtsProviderBase(httpClientFactory, options, concurrency, timeProvider, logger)
{
    public override string Name => AzureOptions.ProviderName;
    protected override string HttpClientName => AzureOptions.HttpClientName;
    protected override string DefaultBaseAddress => throw new InvalidOperationException("Azure requires a configured deployment endpoint.");
    protected override string RelativeSpeechPath => "audio/speech?api-version=preview";

    protected override OpenAICompatibleRequestTarget ResolveTarget(AIResolvedModel model, string relativePath)
        => deployments.Resolve(model, relativePath);
}
