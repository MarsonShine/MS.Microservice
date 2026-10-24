using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.OpenAI;

internal sealed class OpenAIAsrProvider : OpenAICompatibleAsrProviderBase
{
    public OpenAIAsrProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        AIProviderConcurrency concurrency,
        TimeProvider timeProvider,
        ILogger<OpenAIAsrProvider> logger)
        : base(httpClientFactory, options, concurrency, timeProvider, logger)
    {
    }

    public override string Name => OpenAIProviderDefaults.ProviderName;

    protected override string HttpClientName => OpenAIProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => OpenAIProviderDefaults.DefaultBaseAddress;
}