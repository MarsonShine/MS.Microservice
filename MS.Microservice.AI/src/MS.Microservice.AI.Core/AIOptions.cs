namespace MS.Microservice.AI.Core;

public sealed class AIOptions
{
    public const string SectionName = "AI";

    public IDictionary<string, AIProviderRegistrationOptions> Providers { get; } = new Dictionary<string, AIProviderRegistrationOptions>(StringComparer.OrdinalIgnoreCase);

    public AIModelsOptions Models { get; set; } = new();

    public string? DefaultProvider { get; set; }
}

public sealed class AIModelsOptions
{
    public IDictionary<string, AIChatModelOptions> Chat { get; } = new Dictionary<string, AIChatModelOptions>(StringComparer.OrdinalIgnoreCase);
}

public sealed class AIProviderRegistrationOptions
{
    public bool Enabled { get; set; } = true;

    public string? ApiKey { get; set; }

    public string? BaseAddress { get; set; }

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int TimeoutSeconds { get; set; } = 100;

    public int MaxRetryAttempts { get; set; } = 2;

    public int ConcurrencyLimit { get; set; } = 8;
}

public sealed class AIChatModelOptions
{
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public double? Temperature { get; set; }

    public double? TopP { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxRetryAttempts { get; set; }
}