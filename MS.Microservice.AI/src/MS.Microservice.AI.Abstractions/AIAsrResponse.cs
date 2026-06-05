namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The result of an automatic speech recognition / transcription request.
/// </summary>
public sealed record AIAsrResponse
{
    /// <summary>The provider that produced this transcription.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that produced this transcription.</summary>
    public required string Model { get; init; }

    /// <summary>The full transcription text.</summary>
    public required string Text { get; init; }

    /// <summary>Detected or requested language code.</summary>
    public string? Language { get; init; }

    /// <summary>Time-aligned segments when <c>verbose_json</c> format is requested.</summary>
    public IReadOnlyList<AIAsrSegment> Segments { get; init; } = [];

    /// <summary>Token usage reported by the provider, or <see cref="AIUsage.Zero"/> if unavailable.</summary>
    public AIUsage Usage { get; init; } = AIUsage.Zero;

    /// <summary>Provider-assigned request identifier for support/tracing.</summary>
    public string? ProviderRequestId { get; init; }
}