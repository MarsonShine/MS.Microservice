namespace MS.Microservice.AI.Abstractions;

public sealed record AIResolvedModel
{
    public AICapability Capability { get; init; } = AICapability.Chat;

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public string Scenario { get; init; } = "Default";

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? MaxOutputTokens { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    public int MaxRetryAttempts { get; init; }

    public string? Voice { get; init; }

    public string? ResponseFormat { get; init; }

    public double? Speed { get; init; }

    public string? Language { get; init; }

    public string? Prompt { get; init; }

    public string? Size { get; init; }

    public string? Quality { get; init; }

    public int? Count { get; init; }
}