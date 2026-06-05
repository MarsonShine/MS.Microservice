namespace MS.Microservice.AI.Abstractions;

public sealed record AIImageResponse
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required IReadOnlyList<AIImageData> Images { get; init; }

    public AIUsage Usage { get; init; } = AIUsage.Zero;

    public string? ProviderRequestId { get; init; }
}