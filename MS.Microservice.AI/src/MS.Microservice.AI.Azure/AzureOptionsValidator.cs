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
            return ValidateOptionsResult.Fail($"AzureCloud or {path} is required when AddAzure() is registered.");
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

            var alias = getModel(model);
            var isSpeech = capability is AICapability.Tts && azure.Value.Cloud.Tts is not null
                && AzureSpeechTtsOptions.IsSpeechModel(alias);
            if (!enabled || !Supports(capability)
                || (!isSpeech && AzureDeploymentRouter.Select(azure.Value, capability, alias) is null))
            {
                failures.Add($"AI:Models:{capability}:{scenario} requires an enabled Azure target for model '{alias}'.");
            }
        }
    }

    private static void ValidateResourcesAndDeployments(AzureOptions options, ICollection<string> failures)
    {
        const string path = AzureOptions.ConfigurationPath;
        if (options.Cloud.Chat is { } chat)
        {
            ValidateCloudModels(chat, AICapability.Chat, "AzureCloud:CHAT", failures);
        }

        if (options.Cloud.Tts is { } tts)
        {
            if (string.IsNullOrWhiteSpace(tts.Secret) || tts.Secret.Any(char.IsWhiteSpace) || tts.Secret.Any(char.IsControl))
            {
                failures.Add("AzureCloud:TTS:Secret requires a Speech resource key without whitespace or control characters.");
            }

            if (tts.GetSynthesisEndpoint() is null)
            {
                failures.Add("AzureCloud:TTS requires a valid Region and, when supplied, an HTTPS Speech endpoint matching that region.");
            }

            if (AzureDeploymentRouter.Select(options, AICapability.Tts, AzureSpeechTtsOptions.ModelAlias) is not null)
            {
                failures.Add($"'{AzureSpeechTtsOptions.ModelAlias}' is reserved for AzureCloud:TTS when Azure AI Speech is configured.");
            }
        }

        if (options.Cloud.IsConfigured && options.Resources.Count == 0 && options.Deployments.Count == 0)
        {
            return;
        }

        if (options.Resources.Count == 0 || options.Deployments.Count == 0)
        {
            failures.Add($"{path} requires Resources and Deployments.");
        }

        ValidateResources(options.Resources, $"{path}:Resources", "ApiKey", failures);

        foreach (var (capability, aliases) in options.Deployments)
        {
            if (!Supports(capability))
            {
                failures.Add($"{path}:Deployments:{capability} is not supported by Azure.");
            }

            foreach (var (alias, candidates) in aliases)
            {
                foreach (var candidate in candidates)
                {
                    ValidateDeployment(candidate, options.Resources, capability, $"{path}:Deployments:{capability}:{alias}", failures);
                }
            }
        }
    }

    private static void ValidateCloudModels(AzureCloudModelOptions group, AICapability capability, string path, ICollection<string> failures)
    {
        if (group.Resources.Count == 0 || group.Models.Count == 0)
        {
            failures.Add($"{path} requires Resources and Models.");
        }

        ValidateResources(group.Resources, $"{path}:Resources", "Secret", failures);
        foreach (var (alias, deployment) in group.Models)
        {
            ValidateDeployment(deployment, group.Resources, capability, $"{path}:Models:{alias}", failures);
        }
    }

    private static void ValidateResources<T>(IReadOnlyDictionary<string, T> resources, string path, string keyProperty, ICollection<string> failures)
        where T : AzureResourceOptions
    {
        foreach (var (name, resource) in resources)
        {
            if (!resource.Enabled)
            {
                continue;
            }

            if (AzureDeploymentRouter.GetApiBase(resource.Endpoint) is null)
            {
                failures.Add($"{path}:{name}:Endpoint must be an HTTPS resource URL or Azure OpenAI v1 base URL.");
            }

            if (string.IsNullOrWhiteSpace(resource.ApiKey) || resource.ApiKey.Any(char.IsWhiteSpace) || resource.ApiKey.Any(char.IsControl))
            {
                failures.Add($"{path}:{name}:{keyProperty} must resolve to a non-empty key without whitespace or control characters.");
            }
        }
    }

    private static void ValidateDeployment<T>(AzureDeploymentOptions deployment, IReadOnlyDictionary<string, T> resources,
        AICapability capability, string path, ICollection<string> failures)
    {
        if (!deployment.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(deployment.Deployment) || string.IsNullOrWhiteSpace(deployment.Resource)
            || !resources.ContainsKey(deployment.Resource))
        {
            failures.Add($"{path} requires a deployment name and a configured resource for each enabled candidate.");
        }

        if (deployment.Api is { } api && (capability != AICapability.Chat || api is not (AzureDeploymentApi.ChatCompletions or AzureDeploymentApi.Responses)))
        {
            failures.Add($"{path}:Api is not supported for Azure {capability}.");
        }

        if (deployment.MaxTokenParameter is { } parameter && (capability != AICapability.Chat
            || deployment.Api is AzureDeploymentApi.Responses || parameter is not ("max_tokens" or "max_completion_tokens")))
        {
            failures.Add($"{path}:MaxTokenParameter must be max_tokens or max_completion_tokens and applies to Chat Completions only.");
        }
    }

    private static bool IsAzure(string? name) => string.Equals(name, AzureOptions.ProviderName, StringComparison.OrdinalIgnoreCase);

    private static bool Supports(AICapability capability)
        => capability is AICapability.Chat or AICapability.ImageGeneration or AICapability.ImageEdit or AICapability.Tts or AICapability.Asr;
}
