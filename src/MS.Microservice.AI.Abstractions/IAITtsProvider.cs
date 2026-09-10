namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Contract for a concrete text-to-speech provider implementation.
/// </summary>
public interface IAITtsProvider
{
    /// <summary>The unique provider name.</summary>
    string Name { get; }

    /// <summary>
    /// Converts text to speech.
    /// </summary>
    /// <param name="model">Fully resolved model configuration including voice and format.</param>
    /// <param name="request">The original synthesis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synthesized audio payload.</returns>
    ValueTask<AITtsResponse> SynthesizeAsync(AIResolvedModel model, AITtsRequest request, CancellationToken cancellationToken = default);
}