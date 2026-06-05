namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Provider-neutral entry point for chat / text completion.
/// The implementation routes requests through <see cref="IAIModelResolver"/> and
/// <see cref="IAIProviderFactory"/> to the correct <see cref="IAIChatProvider"/>.
/// </summary>
public interface IAIChatClient
{
    /// <summary>
    /// Sends a chat completion request and returns the full response.
    /// </summary>
    /// <param name="request">The chat request. At least one message is required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete chat response with text, usage, and metadata.</returns>
    /// <exception cref="AIConfigurationException">Configuration is missing or invalid.</exception>
    /// <exception cref="AIProviderException">The provider returned an error.</exception>
    ValueTask<AIChatResponse> GetResponseAsync(AIChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion, yielding incremental text chunks.
    /// The final chunk has <see cref="AIChatStreamChunk.IsFinal"/> set to <c>true</c>.
    /// </summary>
    /// <param name="request">The chat request. At least one message is required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of chat chunks.</returns>
    IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIChatRequest request, CancellationToken cancellationToken = default);
}