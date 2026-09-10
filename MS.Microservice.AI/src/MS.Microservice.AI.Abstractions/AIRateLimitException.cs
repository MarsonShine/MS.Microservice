namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Thrown when the provider returns HTTP 429 (Too Many Requests).
/// This is always transient; callers should honor <see cref="AIException.RetryAfter"/> if set.
/// </summary>
public sealed class AIRateLimitException : AIProviderException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIRateLimitException"/>.
    /// </summary>
    public AIRateLimitException(
        string message,
        AICapability capability = AICapability.Chat,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        string? providerRequestId = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(
            message,
            AIErrorCodes.RateLimited,
            capability,
            provider,
            model,
            scenario,
            requestId,
            providerRequestId,
            statusCode,
            isTransient: true,
            retryAfter: retryAfter,
            innerException: innerException)
    {
    }
}