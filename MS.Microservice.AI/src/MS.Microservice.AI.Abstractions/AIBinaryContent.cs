namespace MS.Microservice.AI.Abstractions;

public sealed record AIBinaryContent
{
    public required byte[] Content { get; init; }

    public string? ContentType { get; init; }

    public string? FileName { get; init; }
}