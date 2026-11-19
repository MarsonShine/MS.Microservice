using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureOptions
{
    public const string ProviderName = "Azure";
    public const string HttpClientName = "MS.Microservice.AI.Azure";
    public const string ConfigurationPath = "AI:Providers:Azure";

    public Dictionary<string, AzureResourceOptions> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<AICapability, Dictionary<string, AzureDeploymentOptions[]>> Deployments { get; } = [];
}

internal sealed class AzureResourceOptions
{
    public bool Enabled { get; set; } = true;
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeySecretName { get; set; }
}

internal sealed class AzureDeploymentOptions
{
    public bool Enabled { get; set; } = true;
    public string? Resource { get; set; }
    public string? Deployment { get; set; }
}

internal sealed class AzureResourceSecrets(IAISecretProvider secrets) : IPostConfigureOptions<AzureOptions>
{
    public void PostConfigure(string? name, AzureOptions options)
    {
        foreach (var (resourceName, resource) in options.Resources)
        {
            if (resource.Enabled && string.IsNullOrWhiteSpace(resource.ApiKey))
            {
                resource.ApiKey = secrets.GetSecret(new AISecretRequest
                {
                    Provider = $"Azure__Resources__{resourceName}",
                    SecretName = resource.ApiKeySecretName,
                });
            }
        }
    }
}
