namespace MS.Microservice.AI.Abstractions;

public sealed record AIImageData
{
    public string? Url { get; init; }

    public AIBinaryContent? Content { get; init; }

    public string? RevisedPrompt { get; init; }
}