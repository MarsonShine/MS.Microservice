namespace MS.Microservice.AI.Abstractions;

public class AIProviderException : AIException
{
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