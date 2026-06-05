namespace MS.Microservice.AI.Abstractions;

public interface IAIProviderFactory
{
    IAIChatProvider GetRequiredChatProvider(string providerName);
}