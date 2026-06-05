namespace MS.Microservice.AI.Abstractions;

public sealed class AIContentSafetyException : AIProviderException
{
    public AIContentSafetyException(
        string message,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        string? providerRequestId = null,
        int? statusCode = null,
        Exception? innerException = null)
        : base(
            message,
            AIErrorCodes.ContentFiltered,
            provider,
            model,
            scenario,
            requestId,
            providerRequestId,
            statusCode,
            innerException: innerException)
    {
    }
}