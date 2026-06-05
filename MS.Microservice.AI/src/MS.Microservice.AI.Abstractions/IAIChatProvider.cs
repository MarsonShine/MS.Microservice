namespace MS.Microservice.AI.Abstractions;

public interface IAIChatProvider
{
    string Name { get; }

    ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default);
}