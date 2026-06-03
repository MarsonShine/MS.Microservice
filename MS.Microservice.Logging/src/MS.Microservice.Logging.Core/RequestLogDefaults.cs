namespace MS.Microservice.Logging.Core;

/// <summary>
/// Default names for inbound headers and structured logging properties.
/// </summary>
public static class RequestLogDefaults
{
    public const string RequestIdHeaderName = "requestId";
    public const string PlatformIdHeaderName = "platformId";
    public const string UserFlagHeaderName = "userflag";

    public const string RequestIdPropertyName = "requestId";
    public const string PlatformIdPropertyName = "platformId";
    public const string UserFlagPropertyName = "userflag";
    public const string ElapsedMillisecondsPropertyName = "elapsedMs";
    public const string MethodPropertyName = "requestMethod";
    public const string PathPropertyName = "requestPath";
    public const string StatusCodePropertyName = "statusCode";
}