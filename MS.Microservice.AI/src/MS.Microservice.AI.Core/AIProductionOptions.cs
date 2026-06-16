namespace MS.Microservice.AI.Core;

/// <summary>Options for AI request rate limiting.</summary>
public sealed class AIRateLimitingOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Whether the built-in fixed-window limiter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Maximum requests per provider/model window. <c>null</c> means no built-in limit.</summary>
    public int? RequestsPerWindow { get; set; }

    /// <summary>Window duration in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Options for AI provider circuit breaking.</summary>
public sealed class AICircuitBreakerOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "CircuitBreaker";

    /// <summary>Whether the built-in in-memory circuit breaker is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Consecutive failure threshold before opening the circuit.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long the circuit remains open, in seconds.</summary>
    public int BreakDurationSeconds { get; set; } = 30;
}

/// <summary>Options for AI log sanitization.</summary>
public sealed class AILogSanitizerOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "LogSanitizer";

    /// <summary>Replacement text used for redacted values.</summary>
    public string RedactionText { get; set; } = "[REDACTED]";

    /// <summary>Case-insensitive field names that should be redacted in metadata and simple text payloads.</summary>
    public ISet<string> SensitiveFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api_key",
        "apikey",
        "apiKey",
        "authorization",
        "access_token",
        "token",
        "secret",
        "password",
        "prompt",
        "response",
    };
}

/// <summary>Options for AI secret lookup.</summary>
public sealed class AISecretProviderOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "SecretProvider";

    /// <summary>Whether environment variables are preferred over configuration values.</summary>
    public bool PreferEnvironment { get; set; } = true;

    /// <summary>Environment variable prefix used for derived provider API key names.</summary>
    public string EnvironmentVariablePrefix { get; set; } = "AI__PROVIDERS__";
}

/// <summary>Payload limit options by AI capability.</summary>
public sealed class AIPayloadLimitOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "PayloadLimits";

    /// <summary>Maximum combined chat message characters for non-streaming requests.</summary>
    public int MaxChatCharacters { get; set; } = 200_000;

    /// <summary>Maximum combined chat message characters for streaming requests.</summary>
    public int MaxStreamingChatCharacters { get; set; } = 200_000;

    /// <summary>Maximum text characters for text/audio text inputs.</summary>
    public int MaxTextCharacters { get; set; } = 100_000;

    /// <summary>Maximum audio request bytes.</summary>
    public long MaxAudioBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Maximum image prompt characters.</summary>
    public int MaxImagePromptCharacters { get; set; } = 20_000;

    /// <summary>Maximum source image bytes for image edit requests.</summary>
    public long MaxImageBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>Maximum mask image bytes for image edit requests.</summary>
    public long MaxImageMaskBytes { get; set; } = 20L * 1024 * 1024;
}

/// <summary>Options for AI cost accounting hooks.</summary>
public sealed class AICostAccountingOptions
{
    /// <summary>Configuration section name under <c>AI</c>.</summary>
    public const string SectionName = "CostAccounting";

    /// <summary>Whether the cost reporter hook is invoked by routing clients.</summary>
    public bool Enabled { get; set; } = true;
}
