using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class DefaultAIProviderFactory : IAIProviderFactory
{
    private readonly IReadOnlyDictionary<string, IAIChatProvider> _providers;

    public DefaultAIProviderFactory(IEnumerable<IAIChatProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToDictionary(
            provider => provider.Name,
            provider => provider,
            StringComparer.OrdinalIgnoreCase);
    }

    public IAIChatProvider GetRequiredChatProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (_providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        throw new AIConfigurationException($"AI chat provider '{providerName}' has not been registered in the current service collection.");
    }
}