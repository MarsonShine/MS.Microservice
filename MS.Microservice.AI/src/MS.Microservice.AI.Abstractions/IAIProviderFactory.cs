namespace MS.Microservice.AI.Abstractions;

public interface IAIProviderFactory
{
    IAIChatProvider GetRequiredChatProvider(string providerName);

    IAITtsProvider GetRequiredTtsProvider(string providerName);

    IAIAsrProvider GetRequiredAsrProvider(string providerName);

    IAIImageGenerationProvider GetRequiredImageGenerationProvider(string providerName);

    IAIImageEditProvider GetRequiredImageEditProvider(string providerName);
}