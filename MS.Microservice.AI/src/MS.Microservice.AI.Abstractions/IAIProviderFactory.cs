namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Looks up registered provider implementations by name and capability.
/// Throws <see cref="AIUnsupportedCapabilityException"/> when a provider is known
/// but does not support the requested capability.
/// </summary>
public interface IAIProviderFactory
{
    /// <summary>Returns the chat provider registered under <paramref name="providerName"/>.</summary>
    /// <exception cref="AIConfigurationException">Provider not registered.</exception>
    /// <exception cref="AIUnsupportedCapabilityException">Provider does not support chat.</exception>
    IAIChatProvider GetRequiredChatProvider(string providerName);

    /// <summary>Returns the TTS provider registered under <paramref name="providerName"/>.</summary>
    /// <exception cref="AIConfigurationException">Provider not registered.</exception>
    /// <exception cref="AIUnsupportedCapabilityException">Provider does not support TTS.</exception>
    IAITtsProvider GetRequiredTtsProvider(string providerName);

    /// <summary>Returns the ASR provider registered under <paramref name="providerName"/>.</summary>
    /// <exception cref="AIConfigurationException">Provider not registered.</exception>
    /// <exception cref="AIUnsupportedCapabilityException">Provider does not support ASR.</exception>
    IAIAsrProvider GetRequiredAsrProvider(string providerName);

    /// <summary>Returns the image generation provider registered under <paramref name="providerName"/>.</summary>
    /// <exception cref="AIConfigurationException">Provider not registered.</exception>
    /// <exception cref="AIUnsupportedCapabilityException">Provider does not support image generation.</exception>
    IAIImageGenerationProvider GetRequiredImageGenerationProvider(string providerName);

    /// <summary>Returns the image edit provider registered under <paramref name="providerName"/>.</summary>
    /// <exception cref="AIConfigurationException">Provider not registered.</exception>
    /// <exception cref="AIUnsupportedCapabilityException">Provider does not support image edit.</exception>
    IAIImageEditProvider GetRequiredImageEditProvider(string providerName);
}