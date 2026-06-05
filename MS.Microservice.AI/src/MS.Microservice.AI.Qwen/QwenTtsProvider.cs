using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

internal sealed class QwenTtsProvider : OpenAICompatibleTtsProviderBase
{
    public QwenTtsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger<QwenTtsProvider> logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public override string Name => QwenProviderDefaults.ProviderName;

    protected override string HttpClientName => QwenProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => QwenProviderDefaults.DefaultBaseAddress;
}