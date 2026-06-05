namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Contract for a concrete chat provider implementation.
/// Each provider (OpenAI, DeepSeek, Qwen, etc.) implements this interface
/// and is registered via its own <c>Add*</c> extension method.
/// </summary>
public interface IAIChatProvider
{
    /// <summary>The unique provider name (e.g. "OpenAI", "DeepSeek").</summary>
    string Name { get; }

    /// <summary>
    /// Sends a non-streaming chat completion request to the provider.
    /// </summary>
    /// <param name="model">Fully resolved model configuration.</param>
    /// <param name="request">The original chat request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete chat response.</returns>
    ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion from the provider.
    /// </summary>
    /// <param name="model">Fully resolved model configuration.</param>
    /// <param name="request">The original chat request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of chat chunks.</returns>
    IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default);
}