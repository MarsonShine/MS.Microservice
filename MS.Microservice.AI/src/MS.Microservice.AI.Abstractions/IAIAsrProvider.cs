namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Contract for a concrete automatic speech recognition provider implementation.
/// </summary>
public interface IAIAsrProvider
{
    /// <summary>The unique provider name.</summary>
    string Name { get; }

    /// <summary>
    /// Transcribes audio to text.
    /// </summary>
    /// <param name="model">Fully resolved model configuration.</param>
    /// <param name="request">The original transcription request with audio payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transcription result.</returns>
    ValueTask<AIAsrResponse> RecognizeAsync(AIResolvedModel model, AIAsrRequest request, CancellationToken cancellationToken = default);
}