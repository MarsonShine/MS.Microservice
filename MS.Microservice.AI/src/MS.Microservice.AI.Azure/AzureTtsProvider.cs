using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureTtsProvider(
    IOptions<AzureOptions> azure,
    IOptions<AIOptions> ai,
    AzureOpenAITtsAdapter openAI,
    AzureSpeechTtsAdapter speech) : IAITtsProvider
{
    public string Name => AzureOptions.ProviderName;

    public ValueTask<AITtsResponse> SynthesizeAsync(AIResolvedModel model, AITtsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (azure.Value.Cloud.Tts is not { } tts || !AzureSpeechTtsOptions.IsSpeechModel(model.Model))
        {
            return openAI.SynthesizeAsync(model, request, cancellationToken);
        }

        if (!AIOptionsLookup.TryGetProvider(ai.Value, Name, out var provider) || !provider.Enabled)
        {
            throw new AIConfigurationException("The Azure AI provider is disabled or missing from configuration.");
        }

        return speech.SynthesizeAsync(tts, provider, model, request, cancellationToken);
    }
}

internal sealed class AzureOpenAITtsAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider timeProvider,
    ILogger<AzureOpenAITtsAdapter> logger,
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
