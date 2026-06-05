namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// A single timed segment within a transcription result.
/// </summary>
public sealed record AIAsrSegment
{
    /// <summary>The transcribed text for this segment.</summary>
    public required string Text { get; init; }

    /// <summary>Start time of the segment in seconds.</summary>
    public double? StartSeconds { get; init; }

    /// <summary>End time of the segment in seconds.</summary>
    public double? EndSeconds { get; init; }
}