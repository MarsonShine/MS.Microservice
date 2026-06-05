namespace MS.Microservice.AI.Abstractions;

public interface IAIImageEditProvider
{
    string Name { get; }

    ValueTask<AIImageResponse> EditAsync(AIResolvedModel model, AIImageEditRequest request, CancellationToken cancellationToken = default);
}