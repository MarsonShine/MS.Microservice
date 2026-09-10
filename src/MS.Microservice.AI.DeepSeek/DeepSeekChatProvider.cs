using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.DeepSeek;

internal sealed class DeepSeekChatProvider : OpenAICompatibleChatProviderBase
{
    public DeepSeekChatProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger<DeepSeekChatProvider> logger)
        : base(httpClientFactory, options, timeProvider, logger)
    {
    }

    public override string Name => DeepSeekProviderDefaults.ProviderName;

    protected override string HttpClientName => DeepSeekProviderDefaults.HttpClientName;

    protected override string DefaultBaseAddress => DeepSeekProviderDefaults.DefaultBaseAddress;
}