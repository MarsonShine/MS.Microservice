namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The result of a text-to-speech synthesis request.
/// </summary>
public sealed record AITtsResponse
{
    /// <summary>The provider that produced this audio.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that produced this audio.</summary>
    public required string Model { get; init; }

    /// <summary>The synthesized audio payload and its metadata.</summary>
    public required AIBinaryContent Audio { get; init; }

    /// <summary>The voice used for synthesis.</summary>
    public string? Voice { get; init; }

    /// <summary>The audio format of the returned payload, e.g. <c>mp3</c>.</summary>
    public string? ResponseFormat { get; init; }
}