using Microsoft.Extensions.Options;

namespace MS.Microservice.AI.Core;

internal sealed class AIRateLimitingOptionsValidator : IValidateOptions<AIRateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, AIRateLimitingOptions options)
    {
        var errors = new List<string>();
        if (options.RequestsPerWindow is <= 0)
        {
            errors.Add("AI:RateLimiting:RequestsPerWindow must be greater than 0 when provided.");
        }

        if (options.WindowSeconds <= 0)
        {
            errors.Add("AI:RateLimiting:WindowSeconds must be greater than 0.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed class AICircuitBreakerOptionsValidator : IValidateOptions<AICircuitBreakerOptions>
{
    public ValidateOptionsResult Validate(string? name, AICircuitBreakerOptions options)
    {
        var errors = new List<string>();
        if (options.FailureThreshold <= 0)
        {
            errors.Add("AI:CircuitBreaker:FailureThreshold must be greater than 0.");
        }

        if (options.BreakDurationSeconds <= 0)
        {
            errors.Add("AI:CircuitBreaker:BreakDurationSeconds must be greater than 0.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed class AILogSanitizerOptionsValidator : IValidateOptions<AILogSanitizerOptions>
{
    public ValidateOptionsResult Validate(string? name, AILogSanitizerOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.RedactionText))
        {
            errors.Add("AI:LogSanitizer:RedactionText is required.");
        }

        if (options.SensitiveFields.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("AI:LogSanitizer:SensitiveFields cannot contain empty values.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed class AISecretProviderOptionsValidator : IValidateOptions<AISecretProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, AISecretProviderOptions options)
    {
        return string.IsNullOrWhiteSpace(options.EnvironmentVariablePrefix)
            ? ValidateOptionsResult.Fail("AI:SecretProvider:EnvironmentVariablePrefix is required.")
            : ValidateOptionsResult.Success;
    }
}

internal sealed class AIPayloadLimitOptionsValidator : IValidateOptions<AIPayloadLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, AIPayloadLimitOptions options)
    {
        var errors = new List<string>();
        if (options.MaxChatCharacters <= 0) errors.Add("AI:PayloadLimits:MaxChatCharacters must be greater than 0.");
        if (options.MaxStreamingChatCharacters <= 0) errors.Add("AI:PayloadLimits:MaxStreamingChatCharacters must be greater than 0.");
        if (options.MaxTextCharacters <= 0) errors.Add("AI:PayloadLimits:MaxTextCharacters must be greater than 0.");
        if (options.MaxAudioBytes <= 0) errors.Add("AI:PayloadLimits:MaxAudioBytes must be greater than 0.");
        if (options.MaxImagePromptCharacters <= 0) errors.Add("AI:PayloadLimits:MaxImagePromptCharacters must be greater than 0.");
        if (options.MaxImageBytes <= 0) errors.Add("AI:PayloadLimits:MaxImageBytes must be greater than 0.");
        if (options.MaxImageMaskBytes <= 0) errors.Add("AI:PayloadLimits:MaxImageMaskBytes must be greater than 0.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
