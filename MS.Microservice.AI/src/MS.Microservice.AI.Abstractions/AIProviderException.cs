namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Base class for errors originating from an AI provider. Concrete subclasses
/// such as <see cref="AIRateLimitException"/> and <see cref="AIContentSafetyException"/>
/// allow callers to handle specific failure modes.
/// </summary>
public class AIProviderException : AIException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIProviderException"/>.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="errorCode">One of the canonical codes from <see cref="AIErrorCodes"/>.</param>
    /// <param name="capability">The capability being invoked.</param>
    /// <param name="provider">The logical provider name.</param>
    /// <param name="model">The resolved model identifier.</param>
    /// <param name="scenario">The resolved scenario key.</param>
    /// <param name="requestId">Caller-supplied correlation id.</param>
    /// <param name="providerRequestId">Provider-assigned request identifier.</param>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="isTransient">When <c>true</c>, the operation may succeed if retried.</param>
    /// <param name="retryAfter">Suggested delay before retrying.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AIProviderException(
        string message,
        string errorCode,
        AICapability capability = AICapability.Chat,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        string? providerRequestId = null,
        int? statusCode = null,
        bool isTransient = false,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(
            message,
            errorCode,
            capability,
            provider,
            model,
            scenario,
            requestId,
            providerRequestId,
            statusCode,
            isTransient,
            retryAfter,
            innerException)
    {
    }
}