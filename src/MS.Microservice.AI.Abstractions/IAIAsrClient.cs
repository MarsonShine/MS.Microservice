namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Provider-neutral entry point for automatic speech recognition / transcription.
/// </summary>
public interface IAIAsrClient
{
    /// <summary>
    /// Transcribes audio to text.
    /// </summary>
    /// <param name="request">The transcription request. <see cref="AIAsrRequest.Audio"/> must be non-empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transcription result with text, optional segments, and usage.</returns>
    /// <exception cref="AIConfigurationException">Configuration is missing or invalid.</exception>
    /// <exception cref="AIProviderException">The provider returned an error.</exception>
    ValueTask<AIAsrResponse> RecognizeAsync(AIAsrRequest request, CancellationToken cancellationToken = default);
}