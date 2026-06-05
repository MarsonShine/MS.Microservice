namespace MS.Microservice.AI.Abstractions;

public interface IAITtsProvider
{
    string Name { get; }

    ValueTask<AITtsResponse> SynthesizeAsync(AIResolvedModel model, AITtsRequest request, CancellationToken cancellationToken = default);
}