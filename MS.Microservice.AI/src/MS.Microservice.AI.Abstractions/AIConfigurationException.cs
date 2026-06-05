namespace MS.Microservice.AI.Abstractions;

public sealed class AIConfigurationException : AIException
{
    public AIConfigurationException(string message, Exception? innerException = null)
        : base(message, AIErrorCodes.InvalidConfiguration, innerException: innerException)
    {
    }
}