namespace MS.Microservice.AI.Abstractions;

public sealed record AIResolvedModel
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public string Scenario { get; init; } = "Default";

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? MaxOutputTokens { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    public int MaxRetryAttempts { get; init; }
}