using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

internal sealed class QwenImageGenerationProvider : OpenAICompatibleImageGenerationProviderBase
{
    public QwenImageGenerationProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger<QwenImageGenerationProvider> logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public override string Name => QwenProviderDefaults.ProviderName;

    protected override string HttpClientName => QwenProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => QwenProviderDefaults.DefaultBaseAddress;
}