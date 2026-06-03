namespace MS.Microservice.Logging.Core;

/// <summary>
/// Represents the ambient request logging context shared across providers.
/// </summary>
public sealed class RequestLogContext
{
    public string? RequestId { get; set; }

    public string? PlatformId { get; set; }

    public string? UserFlag { get; set; }

    public string? Method { get; set; }

    public string? Path { get; set; }

    public int? StatusCode { get; set; }

    public long? ElapsedMilliseconds { get; set; }
}