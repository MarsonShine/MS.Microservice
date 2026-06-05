namespace MS.Microservice.AI.Abstractions;

public interface IAIAsrClient
{
    ValueTask<AIAsrResponse> RecognizeAsync(AIAsrRequest request, CancellationToken cancellationToken = default);
}