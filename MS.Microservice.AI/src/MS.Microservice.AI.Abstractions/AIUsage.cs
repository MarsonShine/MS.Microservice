namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Token usage statistics returned by a provider. Use <see cref="Zero"/> when
/// usage data is unavailable.
/// </summary>
public sealed record AIUsage
{
    /// <summary>A zero-valued singleton for cases where usage is not reported.</summary>
    public static AIUsage Zero { get; } = new();

    /// <summary>Number of tokens in the prompt / input.</summary>
    public int InputTokens { get; init; }

    /// <summary>Number of tokens in the completion / output.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total tokens consumed by the request.</summary>
    public int TotalTokens { get; init; }
}