namespace MS.Microservice.AI.Abstractions;

public sealed record AIUsage
{
    public static AIUsage Zero { get; } = new();

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int TotalTokens { get; init; }
}