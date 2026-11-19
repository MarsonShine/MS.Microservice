using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureCloudOptions
{
    public AzureCloudModelOptions? Chat { get; set; }

    public bool IsConfigured => Chat is not null;
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
        if (azure.Value.Cloud.IsConfigured && !AIOptionsLookup.TryGetProvider(options, AzureOptions.ProviderName, out _))
        {
            options.Providers.Add(AzureOptions.ProviderName, new AIProviderRegistrationOptions());
        }
    }
}
