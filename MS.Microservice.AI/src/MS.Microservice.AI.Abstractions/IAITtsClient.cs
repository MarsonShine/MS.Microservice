namespace MS.Microservice.AI.Abstractions;

public interface IAITtsClient
{
    ValueTask<AITtsResponse> SynthesizeAsync(AITtsRequest request, CancellationToken cancellationToken = default);
}