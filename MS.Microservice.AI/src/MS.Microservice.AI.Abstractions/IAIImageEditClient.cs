namespace MS.Microservice.AI.Abstractions;

public interface IAIImageEditClient
{
    ValueTask<AIImageResponse> EditAsync(AIImageEditRequest request, CancellationToken cancellationToken = default);
}