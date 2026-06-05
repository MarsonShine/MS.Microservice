namespace MS.Microservice.AI.Abstractions;

public sealed record AIAsrResponse
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Text { get; init; }

    public string? Language { get; init; }

    public IReadOnlyList<AIAsrSegment> Segments { get; init; } = [];

    public AIUsage Usage { get; init; } = AIUsage.Zero;

    public string? ProviderRequestId { get; init; }
}