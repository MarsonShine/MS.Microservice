using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal static class AIRequestValidator
{
    public static void ValidateChatRequest(AIChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages is null || request.Messages.Count == 0)
        {
            throw new AIConfigurationException("AI chat request must include at least one message.");
        }

        for (var index = 0; index < request.Messages.Count; index++)
        {
            var message = request.Messages[index];
            if (string.IsNullOrWhiteSpace(message.Role))
            {
                throw new AIConfigurationException($"AI chat message at index {index} must specify a role.");
            }

            if (string.IsNullOrWhiteSpace(message.Content))
            {
                throw new AIConfigurationException($"AI chat message at index {index} must specify content.");
            }
        }

        if (request.Temperature is < 0 or >= 2)
        {
            throw new AIConfigurationException("AI chat request temperature must be within [0, 2)." );
        }

        if (request.TopP is <= 0 or > 1)
        {
            throw new AIConfigurationException("AI chat request top_p must be within (0, 1].");
        }

        if (request.MaxOutputTokens is <= 0)
        {
            throw new AIConfigurationException("AI chat request max output tokens must be greater than 0 when provided.");
        }

        if (request.Timeout.HasValue && request.Timeout.Value <= TimeSpan.Zero)
        {
            throw new AIConfigurationException("AI chat request timeout must be greater than 0 when provided.");
        }

        if (request.Provider is not null && string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new AIConfigurationException("AI chat request provider cannot be empty.");
        }

        if (request.Model is not null && string.IsNullOrWhiteSpace(request.Model))
        {
            throw new AIConfigurationException("AI chat request model cannot be empty.");
        }

        if (request.Scenario is not null && string.IsNullOrWhiteSpace(request.Scenario))
        {
            throw new AIConfigurationException("AI chat request scenario cannot be empty.");
        }
    }
}