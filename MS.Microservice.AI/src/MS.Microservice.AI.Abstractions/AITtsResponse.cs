namespace MS.Microservice.AI.Abstractions;

public sealed record AITtsResponse
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required AIBinaryContent Audio { get; init; }

    public string? Voice { get; init; }

    public string? ResponseFormat { get; init; }
}