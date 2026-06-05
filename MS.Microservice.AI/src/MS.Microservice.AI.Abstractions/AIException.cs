namespace MS.Microservice.AI.Abstractions;

public abstract class AIException : Exception
{
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

    public string ErrorCode { get; }

    public AICapability? Capability { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public string? Scenario { get; }

    public string? RequestId { get; }

    public string? ProviderRequestId { get; }

    public int? StatusCode { get; }

    public bool IsTransient { get; }

    public TimeSpan? RetryAfter { get; }
}