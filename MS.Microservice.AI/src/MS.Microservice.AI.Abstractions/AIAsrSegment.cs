namespace MS.Microservice.AI.Abstractions;

public sealed record AIAsrSegment
{
    public required string Text { get; init; }

    public double? StartSeconds { get; init; }

    public double? EndSeconds { get; init; }
}