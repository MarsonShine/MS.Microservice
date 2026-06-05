namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Thrown when a provider request exceeds the configured or per-request timeout.
/// This is always transient and may be retried.
/// </summary>
public sealed class AITimeoutException : AIProviderException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AITimeoutException"/>.
    /// </summary>
    public AITimeoutException(
        string message,
        AICapability capability = AICapability.Chat,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        Exception? innerException = null)
        : base(
            message,
            AIErrorCodes.ProviderTimeout,
            capability,
            provider,
            model,
            scenario,
            requestId,
            isTransient: true,
            innerException: innerException)
    {
    }
}