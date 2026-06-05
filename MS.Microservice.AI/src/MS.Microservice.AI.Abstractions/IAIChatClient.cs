namespace MS.Microservice.AI.Abstractions;

public interface IAIChatClient
{
    ValueTask<AIChatResponse> GetResponseAsync(AIChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIChatRequest request, CancellationToken cancellationToken = default);
}