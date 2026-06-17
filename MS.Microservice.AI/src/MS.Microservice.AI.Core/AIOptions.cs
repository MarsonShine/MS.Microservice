namespace MS.Microservice.AI.Core;

/// <summary>
/// Root configuration section for the AI module. Binds to the <c>AI</c> key
/// in <c>appsettings.json</c> or equivalent configuration source.
/// </summary>
public sealed class AIOptions
{
    /// <summary>The configuration section name: <c>AI</c>.</summary>
    public const string SectionName = "AI";

    /// <summary>Registered provider configurations, keyed by provider name (case-insensitive).</summary>
    public IDictionary<string, AIProviderRegistrationOptions> Providers { get; } = new Dictionary<string, AIProviderRegistrationOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-capability model configurations.</summary>
    public AIModelsOptions Models { get; set; } = new();

    /// <summary>Fallback provider name used when a model entry does not specify its own provider.</summary>
    public string? DefaultProvider { get; set; }
}

/// <summary>
/// Model configurations grouped by capability. Each dictionary maps scenario
/// keys (case-insensitive) to their model options.
/// </summary>
public sealed class AIModelsOptions
{
    /// <summary>Chat / text completion model scenarios.</summary>
    public IDictionary<string, AIChatModelOptions> Chat { get; } = new Dictionary<string, AIChatModelOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Text-to-speech model scenarios.</summary>
    public IDictionary<string, AITtsModelOptions> Tts { get; } = new Dictionary<string, AITtsModelOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Speech recognition model scenarios.</summary>
    public IDictionary<string, AIAsrModelOptions> Asr { get; } = new Dictionary<string, AIAsrModelOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Image generation model scenarios.</summary>
    public IDictionary<string, AIImageModelOptions> ImageGeneration { get; } = new Dictionary<string, AIImageModelOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Image edit model scenarios.</summary>
    public IDictionary<string, AIImageModelOptions> ImageEdit { get; } = new Dictionary<string, AIImageModelOptions>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Configuration for a single AI provider.
/// </summary>
public sealed class AIProviderRegistrationOptions
{
    /// <summary>When <c>false</c>, the provider is skipped during validation and registration.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>API key or bearer token for authentication.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Provider-neutral secret name used to resolve the API key when <see cref="ApiKey" /> is empty.</summary>
    public string? ApiKeySecretName { get; set; }

    /// <summary>Base URL for the provider's API. Falls back to the provider's well-known default when empty.</summary>
    public string? BaseAddress { get; set; }

    /// <summary>Additional HTTP headers sent with every request to this provider.</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default request timeout in seconds. Individual models can override this.</summary>
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>Default maximum retry attempts for transient failures. Individual models can override this.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Maximum concurrent requests allowed for this provider.</summary>
    public int ConcurrencyLimit { get; set; } = 8;
}

/// <summary>
/// Configuration for a chat / text completion model scenario.
/// </summary>
public sealed class AIChatModelOptions
{
    /// <summary>The provider to use for this scenario.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The model identifier (e.g. <c>gpt-4.1-mini</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Default sampling temperature.</summary>
    public double? Temperature { get; set; }

    /// <summary>Default nucleus sampling parameter.</summary>
    public double? TopP { get; set; }

    /// <summary>Default maximum output tokens.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Per-scenario timeout override in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Per-scenario retry override.</summary>
    public int? MaxRetryAttempts { get; set; }
}

/// <summary>
/// Configuration for a text-to-speech model scenario.
/// </summary>
public sealed class AITtsModelOptions
{
    /// <summary>The provider to use for this scenario.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The model identifier (e.g. <c>gpt-4o-mini-tts</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Default voice (e.g. <c>alloy</c>, <c>chelsie</c>).</summary>
    public string? Voice { get; set; }

    /// <summary>Default audio format (e.g. <c>mp3</c>, <c>wav</c>).</summary>
    public string? ResponseFormat { get; set; }

    /// <summary>Default speech speed multiplier.</summary>
    public double? Speed { get; set; }

    /// <summary>Per-scenario timeout override in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Per-scenario retry override.</summary>
    public int? MaxRetryAttempts { get; set; }
}

/// <summary>
/// Configuration for a speech recognition model scenario.
/// </summary>
public sealed class AIAsrModelOptions
{
    /// <summary>The provider to use for this scenario.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The model identifier (e.g. <c>whisper-1</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Default input language hint (ISO-639-1).</summary>
    public string? Language { get; set; }

    /// <summary>Default guiding prompt for transcription.</summary>
    public string? Prompt { get; set; }

    /// <summary>Default response format (e.g. <c>json</c>, <c>verbose_json</c>).</summary>
    public string? ResponseFormat { get; set; }

    /// <summary>Per-scenario timeout override in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Per-scenario retry override.</summary>
    public int? MaxRetryAttempts { get; set; }
}

/// <summary>
/// Configuration for an image generation or image edit model scenario.
/// </summary>
public sealed class AIImageModelOptions
{
    /// <summary>The provider to use for this scenario.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The model identifier (e.g. <c>gpt-image-1</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Default number of images to generate.</summary>
    public int? Count { get; set; }

    /// <summary>Default output dimensions (e.g. <c>1024x1024</c>).</summary>
    public string? Size { get; set; }

    /// <summary>Default quality setting (e.g. <c>standard</c>, <c>hd</c>).</summary>
    public string? Quality { get; set; }

    /// <summary>Default response format (e.g. <c>url</c>, <c>b64_json</c>).</summary>
    public string? ResponseFormat { get; set; }

    /// <summary>Per-scenario timeout override in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Per-scenario retry override.</summary>
    public int? MaxRetryAttempts { get; set; }
}
