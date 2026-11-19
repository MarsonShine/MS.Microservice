using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureCloudOptions
{
    public AzureCloudModelOptions? Chat { get; set; }
    public AzureSpeechTtsOptions? Tts { get; set; }

    public bool IsConfigured => Chat is not null || Tts is not null;
}

internal sealed class AzureCloudModelOptions
{
    public Dictionary<string, AzureCloudResourceOptions> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AzureDeploymentOptions> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class AzureCloudResourceOptions : AzureResourceOptions
{
    public string? Secret { get => ApiKey; set => ApiKey = value; }
}

internal sealed class AzureCloudRegistration(IOptions<AzureOptions> azure) : IPostConfigureOptions<AIOptions>
{
    public void PostConfigure(string? name, AIOptions options)
    {
        var cloud = azure.Value.Cloud;
        if (!cloud.IsConfigured)
        {
            return;
        }

        if (!AIOptionsLookup.TryGetProvider(options, AzureOptions.ProviderName, out var provider))
        {
            provider = new AIProviderRegistrationOptions();
            options.Providers.Add(AzureOptions.ProviderName, provider);
        }

        if (provider.Enabled && cloud.Tts is not null && !options.Models.Tts.ContainsKey("Default"))
        {
            options.Models.Tts.Add("Default", new AITtsModelOptions
            {
                Provider = AzureOptions.ProviderName,
                Model = AzureSpeechTtsOptions.ModelAlias,
            });
        }
    }
}
