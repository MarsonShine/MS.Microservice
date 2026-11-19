using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureAsrProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureAsrProvider> logger,
    AzureDeploymentRouter deployments)
    : OpenAICompatibleAsrProviderBase(httpClientFactory, options, concurrency, timeProvider, logger)
{
    public override string Name => AzureOptions.ProviderName;
    protected override string HttpClientName => AzureOptions.HttpClientName;
    protected override string DefaultBaseAddress => throw new InvalidOperationException("Azure requires a configured deployment endpoint.");
    protected override string RelativeTranscriptionsPath => "audio/transcriptions?api-version=preview";

    protected override OpenAICompatibleRequestTarget ResolveTarget(AIResolvedModel model, string relativePath)
        => deployments.Resolve(model, relativePath);

    protected override string GetTranscriptionResponseFormat(AIResolvedModel model)
    {
        var format = model.ResponseFormat?.Trim().ToLowerInvariant() ?? "json";
        return format is "json" or "verbose_json"
            ? format
            : throw new AIConfigurationException("Azure transcription supports response formats json and verbose_json in this client.");
    }
}
