using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureOptionsValidator(IOptions<AzureOptions> azure) : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        const string path = AzureOptions.ConfigurationPath;
        if (!AIOptionsLookup.TryGetProvider(options, AzureOptions.ProviderName, out var provider))
        {
            return ValidateOptionsResult.Fail($"{path} is required when AddAzure() is registered.");
        }

        var failures = new List<string>();
        ValidateScenarios(AICapability.Chat, options.Models.Chat, model => model.Provider, model => model.Model, provider.Enabled, failures);
        ValidateScenarios(AICapability.ImageGeneration, options.Models.ImageGeneration, model => model.Provider, model => model.Model, provider.Enabled, failures);
        ValidateScenarios(AICapability.ImageEdit, options.Models.ImageEdit, model => model.Provider, model => model.Model, provider.Enabled, failures);
        ValidateScenarios(AICapability.Tts, options.Models.Tts, model => model.Provider, model => model.Model, provider.Enabled, failures);
        ValidateScenarios(AICapability.Asr, options.Models.Asr, model => model.Provider, model => model.Model, provider.Enabled, failures);

        if (provider.Enabled)
        {
            if (provider.Headers.Keys.Any(header => header.Equals("api-key", StringComparison.OrdinalIgnoreCase)
                || header.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Equals("Host", StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{path}:Headers must not contain api-key, Authorization or Host.");
            }

            ValidateResourcesAndDeployments(azure.Value, failures);
        }
        else if (IsAzure(options.DefaultProvider))
        {
            failures.Add($"{path}:Enabled must be true when Azure is the default provider.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateScenarios<T>(AICapability capability, IDictionary<string, T> scenarios,
        Func<T, string> getProvider, Func<T, string> getModel, bool enabled, ICollection<string> failures)
    {
        foreach (var (scenario, model) in scenarios)
        {
            if (!IsAzure(getProvider(model)))
            {
                continue;
            }

            if (!enabled || !Supports(capability) || AzureDeploymentRouter.Select(azure.Value, capability, getModel(model)) is null)
            {
                failures.Add($"AI:Models:{capability}:{scenario} requires an enabled Azure deployment for model '{getModel(model)}'.");
            }
        }
    }

    private static void ValidateResourcesAndDeployments(AzureOptions options, ICollection<string> failures)
    {
        const string path = AzureOptions.ConfigurationPath;
        if (options.Resources.Count == 0 || options.Deployments.Count == 0)
        {
            failures.Add($"{path} requires Resources and Deployments.");
        }

        foreach (var (name, resource) in options.Resources)
        {
            if (!resource.Enabled)
            {
                continue;
            }

            if (AzureDeploymentRouter.GetApiBase(resource.Endpoint) is null)
            {
                failures.Add($"{path}:Resources:{name}:Endpoint must be an HTTPS resource URL or Azure OpenAI v1 base URL.");
            }

            if (string.IsNullOrWhiteSpace(resource.ApiKey) || resource.ApiKey.Any(char.IsWhiteSpace) || resource.ApiKey.Any(char.IsControl))
            {
                failures.Add($"{path}:Resources:{name}:ApiKey must resolve to a non-empty key without whitespace or control characters.");
            }
        }

        foreach (var (capability, aliases) in options.Deployments)
        {
            if (!Supports(capability))
            {
                failures.Add($"{path}:Deployments:{capability} is not supported by Azure.");
            }

            foreach (var (alias, candidates) in aliases)
            {
                foreach (var candidate in candidates.Where(candidate => candidate.Enabled))
                {
                    if (string.IsNullOrWhiteSpace(candidate.Deployment) || string.IsNullOrWhiteSpace(candidate.Resource)
                        || !options.Resources.ContainsKey(candidate.Resource))
                    {
                        failures.Add($"{path}:Deployments:{capability}:{alias} requires a deployment name and a configured resource for each enabled candidate.");
                    }
                }
            }
        }
    }

    private static bool IsAzure(string? name) => string.Equals(name, AzureOptions.ProviderName, StringComparison.OrdinalIgnoreCase);

    private static bool Supports(AICapability capability) => capability is AICapability.Chat;
}
