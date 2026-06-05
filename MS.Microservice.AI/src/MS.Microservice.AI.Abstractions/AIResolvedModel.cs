namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The fully resolved model configuration produced by <see cref="IAIModelResolver"/>.
/// Combines request-level overrides with scenario-specific or default configuration
/// entries and provider-level defaults.
/// </summary>
public sealed record AIResolvedModel
{
    /// <summary>The capability this resolution applies to.</summary>
    public AICapability Capability { get; init; } = AICapability.Chat;

    /// <summary>The logical provider name (e.g. "OpenAI", "Qwen").</summary>
    public required string Provider { get; init; }

    /// <summary>The concrete model identifier to send to the provider API.</summary>
    public required string Model { get; init; }

    /// <summary>The resolved scenario key (defaults to "Default").</summary>
    public string Scenario { get; init; } = "Default";

    /// <summary>Sampling temperature (chat).</summary>
    public double? Temperature { get; init; }

    /// <summary>Nucleus sampling parameter (chat).</summary>
    public double? TopP { get; init; }

    /// <summary>Maximum completion tokens (chat).</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Per-request timeout. Defaults to the provider-level timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>Maximum retry attempts for transient failures.</summary>
    public int MaxRetryAttempts { get; init; }

    // -- TTS / ASR -----------------------------------------------------------------

    /// <summary>Voice identifier (TTS).</summary>
    public string? Voice { get; init; }

    /// <summary>Audio or response format, e.g. <c>mp3</c>, <c>wav</c>, <c>verbose_json</c>.</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Speech speed multiplier (TTS). Values greater than 1.0 produce faster speech.</summary>
    public double? Speed { get; init; }

    /// <summary>Input language hint (ASR), ISO-639-1 format.</summary>
    public string? Language { get; init; }

    /// <summary>Optional guiding prompt (ASR).</summary>
    public string? Prompt { get; init; }

    // -- Image ---------------------------------------------------------------------

    /// <summary>Output image dimensions, e.g. <c>1024x1024</c>.</summary>
    public string? Size { get; init; }

    /// <summary>Image quality, e.g. <c>standard</c> or <c>hd</c>.</summary>
    public string? Quality { get; init; }

    /// <summary>Number of images to generate (defaults to 1 when not specified).</summary>
    public int? Count { get; init; }
}