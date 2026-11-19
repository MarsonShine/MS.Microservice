using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureDeploymentRouter(IOptions<AzureOptions> azure, IOptions<AIOptions> ai)
{
    public OpenAICompatibleRequestTarget Resolve(AIResolvedModel model, string relativePath)
        => ResolveDeployment(model).CreateRequestTarget(relativePath);

    public AzureDeploymentTarget ResolveDeployment(AIResolvedModel model)
    {
        if (!AIOptionsLookup.TryGetProvider(ai.Value, AzureOptions.ProviderName, out var provider) || !provider.Enabled)
        {
            throw new AIConfigurationException("The Azure AI provider is disabled or missing from configuration.");
        }

        return Select(azure.Value, model.Capability, model.Model)
            ?? throw new AIConfigurationException($"No enabled Azure deployment is configured for {model.Capability} model '{model.Model}'.");
    }

    internal static AzureDeploymentTarget? Select(
        AzureOptions options, AICapability capability, string model)
    {
        if (capability is AICapability.Chat && options.Cloud.Chat is { } chat)
        {
            return chat.Models.TryGetValue(model, out var deployment) && deployment.Enabled && deployment.Resource is not null
                && chat.Resources.TryGetValue(deployment.Resource, out var resource) && resource.Enabled
                ? new AzureDeploymentTarget(deployment, resource)
                : null;
        }

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
                    return new AzureDeploymentTarget(candidate, resource);
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

internal readonly record struct AzureDeploymentTarget(AzureDeploymentOptions Deployment, AzureResourceOptions Resource)
{
    public OpenAICompatibleRequestTarget CreateRequestTarget(string relativePath)
        => new(new Uri(AzureDeploymentRouter.GetApiBase(Resource.Endpoint!)!, relativePath),
            Deployment.Deployment!, Resource.ApiKey, "api-key");
}
