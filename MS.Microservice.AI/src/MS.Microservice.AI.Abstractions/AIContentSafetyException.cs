namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Thrown when the provider's content safety filter blocks the request or response.
/// This is not retryable without changing the content.
/// </summary>
public sealed class AIContentSafetyException : AIProviderException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIContentSafetyException"/>.
    /// </summary>
    public AIContentSafetyException(
        string message,
        AICapability capability = AICapability.Chat,
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
            capability,
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