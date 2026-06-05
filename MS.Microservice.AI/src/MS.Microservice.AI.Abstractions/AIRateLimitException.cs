namespace MS.Microservice.AI.Abstractions;

public sealed class AIRateLimitException : AIProviderException
{
    public AIRateLimitException(
        string message,
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