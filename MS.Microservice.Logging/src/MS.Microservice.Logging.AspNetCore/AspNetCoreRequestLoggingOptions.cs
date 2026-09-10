using MS.Microservice.Logging.Core;

namespace MS.Microservice.Logging.AspNetCore;

/// <summary>
/// Configures how ASP.NET Core requests are captured into the ambient logging context.
/// </summary>
public sealed class AspNetCoreRequestLoggingOptions
{
    public string RequestIdHeaderName { get; set; } = RequestLogDefaults.RequestIdHeaderName;

    public string PlatformIdHeaderName { get; set; } = RequestLogDefaults.PlatformIdHeaderName;

    public string UserFlagHeaderName { get; set; } = RequestLogDefaults.UserFlagHeaderName;

    public bool EmitCompletionLog { get; set; } = true;
}