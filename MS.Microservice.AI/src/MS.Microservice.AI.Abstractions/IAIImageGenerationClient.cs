namespace MS.Microservice.AI.Abstractions;

public interface IAIImageGenerationClient
{
    ValueTask<AIImageResponse> GenerateAsync(AIImageGenerationRequest request, CancellationToken cancellationToken = default);
}