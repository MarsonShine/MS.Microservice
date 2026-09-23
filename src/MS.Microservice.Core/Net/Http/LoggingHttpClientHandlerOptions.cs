namespace MS.Microservice.Core.Net.Http;

public sealed class LoggingHttpClientHandlerOptions
{
    /// <summary>When enabled, log only HTTP metadata and omit query strings and bodies.</summary>
    public bool EnableRedaction { get; set; }
}
