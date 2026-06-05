namespace MS.Microservice.AI.Abstractions;

public interface IAIAsrProvider
{
    string Name { get; }

    ValueTask<AIAsrResponse> RecognizeAsync(AIResolvedModel model, AIAsrRequest request, CancellationToken cancellationToken = default);
}