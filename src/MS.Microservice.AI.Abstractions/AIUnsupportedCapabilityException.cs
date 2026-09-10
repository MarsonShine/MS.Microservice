namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Thrown when the resolved provider does not support the requested capability.
/// For example, DeepSeek is currently chat-only and will throw this for TTS, ASR,
/// or image requests.
/// </summary>
public sealed class AIUnsupportedCapabilityException : AIProviderException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIUnsupportedCapabilityException"/>.
    /// </summary>
    /// <param name="capability">The capability that is not supported.</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="provider">The logical provider name.</param>
    /// <param name="model">The resolved model identifier.</param>
    /// <param name="scenario">The resolved scenario key.</param>
    /// <param name="requestId">Caller-supplied correlation id.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AIUnsupportedCapabilityException(
        AICapability capability,
        string message,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        Exception? innerException = null)
        : base(
            message,
            AIErrorCodes.UnsupportedCapability,
            capability,
            provider,
            model,
            scenario,
            requestId,
            innerException: innerException)
    {
    }
}