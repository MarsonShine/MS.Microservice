using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal static class AIRequestValidator
{
    public static void ValidateChatRequest(AIChatRequest request, AIPayloadLimitOptions? payloadLimits = null, bool isStreaming = false)
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

        var maxCharacters = isStreaming ? payloadLimits?.MaxStreamingChatCharacters : payloadLimits?.MaxChatCharacters;
        ValidateCharacterLimit(request.Messages.Sum(message => message.Content.Length), maxCharacters, "AI chat request content");

        if (request.Temperature is < 0 or >= 2)
        {
            throw new AIConfigurationException("AI chat request temperature must be within [0, 2).");
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

    public static void ValidateTtsRequest(AITtsRequest request, AIPayloadLimitOptions? payloadLimits = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            throw new AIConfigurationException("AI TTS request must include non-empty input text.");
        }

        ValidateCharacterLimit(request.Input.Length, payloadLimits?.MaxTextCharacters, "AI TTS request input");
        ValidateCommonRequestFields(request.Provider, request.Model, request.Scenario);

        if (request.Voice is not null && string.IsNullOrWhiteSpace(request.Voice))
        {
            throw new AIConfigurationException("AI TTS request voice cannot be empty.");
        }

        if (request.ResponseFormat is not null && string.IsNullOrWhiteSpace(request.ResponseFormat))
        {
            throw new AIConfigurationException("AI TTS request response format cannot be empty.");
        }

        if (request.Speed is <= 0)
        {
            throw new AIConfigurationException("AI TTS request speed must be greater than 0 when provided.");
        }

        ValidateTimeout(request.Timeout);
    }

    public static void ValidateAsrRequest(AIAsrRequest request, AIPayloadLimitOptions? payloadLimits = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBinaryContent(request.Audio, "AI ASR request audio");
        ValidateByteLimit(request.Audio.Content.LongLength, payloadLimits?.MaxAudioBytes, "AI ASR request audio");
        ValidateCommonRequestFields(request.Provider, request.Model, request.Scenario);

        if (request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
        {
            throw new AIConfigurationException("AI ASR request language cannot be empty.");
        }

        if (request.Prompt is not null && string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new AIConfigurationException("AI ASR request prompt cannot be empty.");
        }

        if (request.ResponseFormat is not null && string.IsNullOrWhiteSpace(request.ResponseFormat))
        {
            throw new AIConfigurationException("AI ASR request response format cannot be empty.");
        }

        ValidateTimeout(request.Timeout);
    }

    public static void ValidateImageGenerationRequest(AIImageGenerationRequest request, AIPayloadLimitOptions? payloadLimits = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new AIConfigurationException("AI image generation request must include a prompt.");
        }

        ValidateCharacterLimit(request.Prompt.Length, payloadLimits?.MaxImagePromptCharacters, "AI image generation request prompt");
        ValidateCommonRequestFields(request.Provider, request.Model, request.Scenario);
        ValidateImageRequestFields(request.Count, request.Size, request.Quality, request.ResponseFormat, request.Timeout);
    }

    public static void ValidateImageEditRequest(AIImageEditRequest request, AIPayloadLimitOptions? payloadLimits = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new AIConfigurationException("AI image edit request must include a prompt.");
        }

        ValidateCharacterLimit(request.Prompt.Length, payloadLimits?.MaxImagePromptCharacters, "AI image edit request prompt");
        ValidateBinaryContent(request.Image, "AI image edit request image");
        ValidateByteLimit(request.Image.Content.LongLength, payloadLimits?.MaxImageBytes, "AI image edit request image");

        if (request.Mask is not null)
        {
            ValidateBinaryContent(request.Mask, "AI image edit request mask");
            ValidateByteLimit(request.Mask.Content.LongLength, payloadLimits?.MaxImageMaskBytes, "AI image edit request mask");
        }

        ValidateCommonRequestFields(request.Provider, request.Model, request.Scenario);
        ValidateImageRequestFields(request.Count, request.Size, request.Quality, request.ResponseFormat, request.Timeout);
    }

    private static void ValidateCharacterLimit(int length, int? maxLength, string fieldName)
    {
        if (maxLength is not null && length > maxLength.Value)
        {
            throw new AIConfigurationException($"{fieldName} exceeds the configured limit of {maxLength.Value} characters.");
        }
    }

    private static void ValidateByteLimit(long length, long? maxLength, string fieldName)
    {
        if (maxLength is not null && length > maxLength.Value)
        {
            throw new AIConfigurationException($"{fieldName} exceeds the configured limit of {maxLength.Value} bytes.");
        }
    }

    private static void ValidateImageRequestFields(int? count, string? size, string? quality, string? responseFormat, TimeSpan? timeout)
    {
        if (count is <= 0)
        {
            throw new AIConfigurationException("AI image request count must be greater than 0.");
        }

        if (size is not null && string.IsNullOrWhiteSpace(size))
        {
            throw new AIConfigurationException("AI image request size cannot be empty.");
        }

        if (quality is not null && string.IsNullOrWhiteSpace(quality))
        {
            throw new AIConfigurationException("AI image request quality cannot be empty.");
        }

        if (responseFormat is not null && string.IsNullOrWhiteSpace(responseFormat))
        {
            throw new AIConfigurationException("AI image request response format cannot be empty.");
        }

        ValidateTimeout(timeout);
    }

    private static void ValidateBinaryContent(AIBinaryContent content, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Content.Length == 0)
        {
            throw new AIConfigurationException($"{fieldName} must include binary content.");
        }
    }

    private static void ValidateCommonRequestFields(string? provider, string? model, string? scenario)
    {
        if (provider is not null && string.IsNullOrWhiteSpace(provider))
        {
            throw new AIConfigurationException("AI request provider cannot be empty.");
        }

        if (model is not null && string.IsNullOrWhiteSpace(model))
        {
            throw new AIConfigurationException("AI request model cannot be empty.");
        }

        if (scenario is not null && string.IsNullOrWhiteSpace(scenario))
        {
            throw new AIConfigurationException("AI request scenario cannot be empty.");
        }
    }

    private static void ValidateTimeout(TimeSpan? timeout)
    {
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new AIConfigurationException("AI request timeout must be greater than 0 when provided.");
        }
    }
}
