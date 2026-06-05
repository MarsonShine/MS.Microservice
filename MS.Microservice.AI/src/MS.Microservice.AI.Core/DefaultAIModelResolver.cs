using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

public sealed class DefaultAIModelResolver : IAIModelResolver
{
    private const string DefaultScenario = "Default";
    private readonly IOptions<AIOptions> _options;

    public DefaultAIModelResolver(IOptions<AIOptions> options)
    {
        _options = options;
    }

    public AIResolvedModel ResolveChatModel(AIChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var explicitProvider = request.Provider?.Trim();
        var explicitModel = request.Model?.Trim();

        if (explicitProvider is not null && explicitModel is null)
        {
            throw new AIConfigurationException("AI chat request must specify a model when provider override is used.");
        }

        AIChatModelOptions? configuredModel = null;
        var scenario = request.Scenario?.Trim();

        if (explicitModel is null)
        {
            if (!string.IsNullOrWhiteSpace(scenario)
                && AIOptionsLookup.TryGetChatModel(options, scenario, out var scenarioEntry))
            {
                configuredModel = scenarioEntry.Value;
                scenario = scenarioEntry.Key;
            }
            else if (AIOptionsLookup.TryGetChatModel(options, DefaultScenario, out var defaultEntry))
            {
                configuredModel = defaultEntry.Value;
                scenario = defaultEntry.Key;
            }
            else
            {
                throw new AIConfigurationException("AI chat model resolution failed because no chat scenario or default model is configured.");
            }
        }

        var providerName = explicitProvider
            ?? configuredModel?.Provider
            ?? options.DefaultProvider?.Trim()
            ?? throw new AIConfigurationException("AI chat provider resolution failed because no provider could be determined.");

        if (!AIOptionsLookup.TryGetProvider(options, providerName, out var providerOptions))
        {
            throw new AIConfigurationException($"AI chat provider '{providerName}' is not configured.");
        }

        var modelName = explicitModel
            ?? configuredModel?.Model
            ?? throw new AIConfigurationException("AI chat model resolution failed because no model could be determined.");

        return new AIResolvedModel
        {
            Capability = AICapability.Chat,
            Provider = providerName,
            Model = modelName,
            Scenario = scenario ?? DefaultScenario,
            Temperature = request.Temperature ?? configuredModel?.Temperature,
            TopP = request.TopP ?? configuredModel?.TopP,
            MaxOutputTokens = request.MaxOutputTokens ?? configuredModel?.MaxOutputTokens,
            Timeout = request.Timeout ?? TimeSpan.FromSeconds(configuredModel?.TimeoutSeconds ?? providerOptions.TimeoutSeconds),
            MaxRetryAttempts = configuredModel?.MaxRetryAttempts ?? providerOptions.MaxRetryAttempts,
        };
    }

    public AIResolvedModel ResolveTtsModel(AITtsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var (providerOptions, configuredModel, providerName, modelName, scenario) = ResolveCore(
            request.Provider,
            request.Model,
            request.Scenario,
            options.Models.Tts,
            model => model.Provider,
            model => model.Model);

        return new AIResolvedModel
        {
            Capability = AICapability.Tts,
            Provider = providerName,
            Model = modelName,
            Scenario = scenario,
            Timeout = request.Timeout ?? TimeSpan.FromSeconds(configuredModel?.TimeoutSeconds ?? providerOptions.TimeoutSeconds),
            MaxRetryAttempts = configuredModel?.MaxRetryAttempts ?? providerOptions.MaxRetryAttempts,
            Voice = request.Voice ?? configuredModel?.Voice,
            ResponseFormat = request.ResponseFormat ?? configuredModel?.ResponseFormat,
            Speed = request.Speed ?? configuredModel?.Speed,
        };
    }

    public AIResolvedModel ResolveAsrModel(AIAsrRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var (providerOptions, configuredModel, providerName, modelName, scenario) = ResolveCore(
            request.Provider,
            request.Model,
            request.Scenario,
            options.Models.Asr,
            model => model.Provider,
            model => model.Model);

        return new AIResolvedModel
        {
            Capability = AICapability.Asr,
            Provider = providerName,
            Model = modelName,
            Scenario = scenario,
            Timeout = request.Timeout ?? TimeSpan.FromSeconds(configuredModel?.TimeoutSeconds ?? providerOptions.TimeoutSeconds),
            MaxRetryAttempts = configuredModel?.MaxRetryAttempts ?? providerOptions.MaxRetryAttempts,
            Language = request.Language ?? configuredModel?.Language,
            Prompt = request.Prompt ?? configuredModel?.Prompt,
            ResponseFormat = request.ResponseFormat ?? configuredModel?.ResponseFormat,
        };
    }

    public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var (providerOptions, configuredModel, providerName, modelName, scenario) = ResolveCore(
            request.Provider,
            request.Model,
            request.Scenario,
            options.Models.ImageGeneration,
            model => model.Provider,
            model => model.Model);

        return new AIResolvedModel
        {
            Capability = AICapability.ImageGeneration,
            Provider = providerName,
            Model = modelName,
            Scenario = scenario,
            Timeout = request.Timeout ?? TimeSpan.FromSeconds(configuredModel?.TimeoutSeconds ?? providerOptions.TimeoutSeconds),
            MaxRetryAttempts = configuredModel?.MaxRetryAttempts ?? providerOptions.MaxRetryAttempts,
            Count = request.Count ?? configuredModel?.Count,
            Size = request.Size ?? configuredModel?.Size,
            Quality = request.Quality ?? configuredModel?.Quality,
            ResponseFormat = request.ResponseFormat ?? configuredModel?.ResponseFormat,
        };
    }

    public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var (providerOptions, configuredModel, providerName, modelName, scenario) = ResolveCore(
            request.Provider,
            request.Model,
            request.Scenario,
            options.Models.ImageEdit,
            model => model.Provider,
            model => model.Model);

        return new AIResolvedModel
        {
            Capability = AICapability.ImageEdit,
            Provider = providerName,
            Model = modelName,
            Scenario = scenario,
            Timeout = request.Timeout ?? TimeSpan.FromSeconds(configuredModel?.TimeoutSeconds ?? providerOptions.TimeoutSeconds),
            MaxRetryAttempts = configuredModel?.MaxRetryAttempts ?? providerOptions.MaxRetryAttempts,
            Count = request.Count ?? configuredModel?.Count,
            Size = request.Size ?? configuredModel?.Size,
            Quality = request.Quality ?? configuredModel?.Quality,
            ResponseFormat = request.ResponseFormat ?? configuredModel?.ResponseFormat,
        };
    }

    private (AIProviderRegistrationOptions ProviderOptions, TModelOptions? ModelOptions, string ProviderName, string ModelName, string Scenario) ResolveCore<TModelOptions>(
        string? explicitProvider,
        string? explicitModel,
        string? explicitScenario,
        IDictionary<string, TModelOptions> configuredModels,
        Func<TModelOptions, string> getProvider,
        Func<TModelOptions, string> getModel)
        where TModelOptions : class
    {
        var options = _options.Value;
        var providerOverride = explicitProvider?.Trim();
        var modelOverride = explicitModel?.Trim();

        if (providerOverride is not null && modelOverride is null)
        {
            throw new AIConfigurationException("AI request must specify a model when provider override is used.");
        }

        TModelOptions? configuredModel = null;
        var scenario = explicitScenario?.Trim();

        if (modelOverride is null)
        {
            if (!string.IsNullOrWhiteSpace(scenario)
                && TryGetScenario(configuredModels, scenario, out var scenarioEntry))
            {
                configuredModel = scenarioEntry.Value;
                scenario = scenarioEntry.Key;
            }
            else if (TryGetScenario(configuredModels, DefaultScenario, out var defaultEntry))
            {
                configuredModel = defaultEntry.Value;
                scenario = defaultEntry.Key;
            }
            else
            {
                throw new AIConfigurationException("AI model resolution failed because no scenario-specific or default model is configured.");
            }
        }

        var providerName = providerOverride
            ?? (configuredModel is null ? null : getProvider(configuredModel))
            ?? options.DefaultProvider?.Trim()
            ?? throw new AIConfigurationException("AI provider resolution failed because no provider could be determined.");

        if (!AIOptionsLookup.TryGetProvider(options, providerName, out var providerOptions))
        {
            throw new AIConfigurationException($"AI provider '{providerName}' is not configured.");
        }

        var modelName = modelOverride
            ?? (configuredModel is null ? null : getModel(configuredModel))
            ?? throw new AIConfigurationException("AI model resolution failed because no model could be determined.");

        return (providerOptions, configuredModel, providerName, modelName, scenario ?? DefaultScenario);
    }

    private static bool TryGetScenario<TModelOptions>(
        IDictionary<string, TModelOptions> configuredModels,
        string scenario,
        out KeyValuePair<string, TModelOptions> entry)
        where TModelOptions : class
    {
        foreach (var item in configuredModels)
        {
            if (string.Equals(item.Key, scenario, StringComparison.OrdinalIgnoreCase))
            {
                entry = item;
                return true;
            }
        }

        entry = default;
        return false;
    }
}