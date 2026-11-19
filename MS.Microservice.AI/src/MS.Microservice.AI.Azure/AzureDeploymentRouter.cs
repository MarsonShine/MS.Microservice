using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureDeploymentRouter(IOptions<AzureOptions> azure, IOptions<AIOptions> ai)
{
    public OpenAICompatibleRequestTarget Resolve(AIResolvedModel model, string relativePath)
    {
        if (!AIOptionsLookup.TryGetProvider(ai.Value, AzureOptions.ProviderName, out var provider) || !provider.Enabled)
        {
            throw new AIConfigurationException("The Azure AI provider is disabled or missing from configuration.");
        }

        var target = Select(azure.Value, model.Capability, model.Model);
        if (target is null)
        {
            throw new AIConfigurationException($"No enabled Azure deployment is configured for {model.Capability} model '{model.Model}'.");
        }

        var (deployment, resource) = target.Value;
        return new OpenAICompatibleRequestTarget(new Uri(GetApiBase(resource.Endpoint!)!, relativePath),
            deployment.Deployment!, resource.ApiKey, "api-key");
    }

    internal static (AzureDeploymentOptions Deployment, AzureResourceOptions Resource)? Select(
        AzureOptions options, AICapability capability, string model)
    {
        if (!options.Deployments.TryGetValue(capability, out var aliases))
        {
            return null;
        }

        foreach (var (alias, candidates) in aliases)
        {
            if (!string.Equals(alias, model, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Enabled && candidate.Resource is not null
                    && options.Resources.TryGetValue(candidate.Resource, out var resource) && resource.Enabled)
                {
                    return (candidate, resource);
                }
            }
        }

        return null;
    }

    internal static Uri? GetApiBase(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath is not ("/" or "/openai/v1" or "/openai/v1/"))
        {
            return null;
        }

        return new Uri(uri, "/openai/v1/");
    }
}
