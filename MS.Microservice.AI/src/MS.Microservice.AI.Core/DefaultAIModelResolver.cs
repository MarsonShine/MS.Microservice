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
}