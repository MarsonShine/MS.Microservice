namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Abstract base class for all AI module exceptions. Carries structured
/// diagnostic information that callers can use for logging, retry decisions,
/// and error-response mapping.
/// </summary>
public abstract class AIException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIException"/>.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="errorCode">One of the canonical codes from <see cref="AIErrorCodes"/>.</param>
    /// <param name="capability">The capability being invoked when the error occurred.</param>
    /// <param name="provider">The logical provider name.</param>
    /// <param name="model">The resolved model identifier.</param>
    /// <param name="scenario">The resolved scenario key.</param>
    /// <param name="requestId">Caller-supplied correlation id.</param>
    /// <param name="providerRequestId">Provider-assigned request identifier.</param>
    /// <param name="statusCode">HTTP status code when the error originated from an HTTP call.</param>
    /// <param name="isTransient">When <c>true</c>, the operation may succeed if retried.</param>
    /// <param name="retryAfter">Suggested delay before retrying.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    protected AIException(
        string message,
        string errorCode,
        AICapability? capability = null,
        string? provider = null,
        string? model = null,
        string? scenario = null,
        string? requestId = null,
        string? providerRequestId = null,
        int? statusCode = null,
        bool isTransient = false,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Capability = capability;
        Provider = provider;
        Model = model;
        Scenario = scenario;
        RequestId = requestId;
        ProviderRequestId = providerRequestId;
        StatusCode = statusCode;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }

    /// <summary>Canonical error code from <see cref="AIErrorCodes"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>The capability being invoked when the error occurred.</summary>
    public AICapability? Capability { get; }

    /// <summary>The logical provider name.</summary>
    public string? Provider { get; }

    /// <summary>The resolved model identifier.</summary>
    public string? Model { get; }

    /// <summary>The resolved scenario key.</summary>
    public string? Scenario { get; }

    /// <summary>Caller-supplied correlation id.</summary>
    public string? RequestId { get; }

    /// <summary>Provider-assigned request identifier for support/tracing.</summary>
    public string? ProviderRequestId { get; }

    /// <summary>HTTP status code when the error originated from an HTTP call.</summary>
    public int? StatusCode { get; }

    /// <summary>When <c>true</c>, the operation may succeed if retried.</summary>
    public bool IsTransient { get; }

    /// <summary>Suggested delay before retrying, if provided by the provider.</summary>
    public TimeSpan? RetryAfter { get; }
}