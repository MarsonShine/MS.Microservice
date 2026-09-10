using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class DefaultAIProviderFactory : IAIProviderFactory
{
    private readonly IReadOnlyDictionary<string, IAIChatProvider> _chatProviders;
    private readonly IReadOnlyDictionary<string, IAITtsProvider> _ttsProviders;
    private readonly IReadOnlyDictionary<string, IAIAsrProvider> _asrProviders;
    private readonly IReadOnlyDictionary<string, IAIImageGenerationProvider> _imageGenerationProviders;
    private readonly IReadOnlyDictionary<string, IAIImageEditProvider> _imageEditProviders;
    private readonly HashSet<string> _knownProviders;

    public DefaultAIProviderFactory(
        IEnumerable<IAIChatProvider> chatProviders,
        IEnumerable<IAITtsProvider> ttsProviders,
        IEnumerable<IAIAsrProvider> asrProviders,
        IEnumerable<IAIImageGenerationProvider> imageGenerationProviders,
        IEnumerable<IAIImageEditProvider> imageEditProviders)
    {
        ArgumentNullException.ThrowIfNull(chatProviders);
        ArgumentNullException.ThrowIfNull(ttsProviders);
        ArgumentNullException.ThrowIfNull(asrProviders);
        ArgumentNullException.ThrowIfNull(imageGenerationProviders);
        ArgumentNullException.ThrowIfNull(imageEditProviders);

        _chatProviders = chatProviders.ToDictionary(
            provider => provider.Name,
            provider => provider,
            StringComparer.OrdinalIgnoreCase);
        _ttsProviders = ttsProviders.ToDictionary(provider => provider.Name, provider => provider, StringComparer.OrdinalIgnoreCase);
        _asrProviders = asrProviders.ToDictionary(provider => provider.Name, provider => provider, StringComparer.OrdinalIgnoreCase);
        _imageGenerationProviders = imageGenerationProviders.ToDictionary(provider => provider.Name, provider => provider, StringComparer.OrdinalIgnoreCase);
        _imageEditProviders = imageEditProviders.ToDictionary(provider => provider.Name, provider => provider, StringComparer.OrdinalIgnoreCase);

        _knownProviders =
        [
            .. _chatProviders.Keys,
            .. _ttsProviders.Keys,
            .. _asrProviders.Keys,
            .. _imageGenerationProviders.Keys,
            .. _imageEditProviders.Keys,
        ];
    }

    public IAIChatProvider GetRequiredChatProvider(string providerName)
    {
        return GetRequiredProvider(providerName, _chatProviders, AICapability.Chat);
    }

    public IAITtsProvider GetRequiredTtsProvider(string providerName)
    {
        return GetRequiredProvider(providerName, _ttsProviders, AICapability.Tts);
    }

    public IAIAsrProvider GetRequiredAsrProvider(string providerName)
    {
        return GetRequiredProvider(providerName, _asrProviders, AICapability.Asr);
    }

    public IAIImageGenerationProvider GetRequiredImageGenerationProvider(string providerName)
    {
        return GetRequiredProvider(providerName, _imageGenerationProviders, AICapability.ImageGeneration);
    }

    public IAIImageEditProvider GetRequiredImageEditProvider(string providerName)
    {
        return GetRequiredProvider(providerName, _imageEditProviders, AICapability.ImageEdit);
    }

    private TProvider GetRequiredProvider<TProvider>(
        string providerName,
        IReadOnlyDictionary<string, TProvider> providers,
        AICapability capability)
        where TProvider : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        if (_knownProviders.Contains(providerName))
        {
            throw new AIUnsupportedCapabilityException(capability, $"AI provider '{providerName}' does not support capability '{capability}'.", provider: providerName);
        }

        throw new AIConfigurationException($"AI provider '{providerName}' has not been registered in the current service collection.");
    }
}