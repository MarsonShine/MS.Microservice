namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Thrown when the AI configuration is missing, incomplete, or invalid.
/// This is always a non-transient error that requires operator intervention.
/// </summary>
public sealed class AIConfigurationException : AIException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIConfigurationException"/>.
    /// </summary>
    /// <param name="message">Human-readable description of the configuration problem.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AIConfigurationException(string message, Exception? innerException = null)
        : base(message, AIErrorCodes.InvalidConfiguration, innerException: innerException)
    {
    }
}