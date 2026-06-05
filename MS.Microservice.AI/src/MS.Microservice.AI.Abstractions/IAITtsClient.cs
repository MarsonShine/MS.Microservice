namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Provider-neutral entry point for text-to-speech synthesis.
/// </summary>
public interface IAITtsClient
{
    /// <summary>
    /// Converts text to speech and returns the audio payload.
    /// </summary>
    /// <param name="request">The synthesis request. <see cref="AITtsRequest.Input"/> must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synthesized audio with metadata.</returns>
    /// <exception cref="AIConfigurationException">Configuration is missing or invalid.</exception>
    /// <exception cref="AIProviderException">The provider returned an error.</exception>
    ValueTask<AITtsResponse> SynthesizeAsync(AITtsRequest request, CancellationToken cancellationToken = default);
}