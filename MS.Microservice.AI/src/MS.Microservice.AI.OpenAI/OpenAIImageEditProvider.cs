using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.OpenAI;

internal sealed class OpenAIImageEditProvider : OpenAICompatibleImageEditProviderBase
{
    public OpenAIImageEditProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger<OpenAIImageEditProvider> logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public override string Name => OpenAIProviderDefaults.ProviderName;

    protected override string HttpClientName => OpenAIProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => OpenAIProviderDefaults.DefaultBaseAddress;
}