namespace MS.Microservice.AI.Abstractions;

public interface IAIImageGenerationProvider
{
    string Name { get; }

    ValueTask<AIImageResponse> GenerateAsync(AIResolvedModel model, AIImageGenerationRequest request, CancellationToken cancellationToken = default);
}