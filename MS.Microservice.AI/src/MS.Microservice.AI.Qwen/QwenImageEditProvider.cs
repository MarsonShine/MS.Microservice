using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

internal sealed class QwenImageEditProvider : OpenAICompatibleImageEditProviderBase
{
    public QwenImageEditProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger<QwenImageEditProvider> logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public override string Name => QwenProviderDefaults.ProviderName;

    protected override string HttpClientName => QwenProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => QwenProviderDefaults.DefaultBaseAddress;
}