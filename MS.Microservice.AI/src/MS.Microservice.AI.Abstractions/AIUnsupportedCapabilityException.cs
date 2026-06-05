namespace MS.Microservice.AI.Abstractions;

public sealed class AIUnsupportedCapabilityException : AIProviderException
{
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