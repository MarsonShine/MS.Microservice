namespace MS.Microservice.AI.Abstractions;

public sealed class AITimeoutException : AIProviderException
{
    public AITimeoutException(
        string message,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        Exception? innerException = null)
        : base(
            message,
            AIErrorCodes.ProviderTimeout,
            provider,
            model,
            scenario,
            requestId,
            isTransient: true,
            innerException: innerException)
    {
    }
}