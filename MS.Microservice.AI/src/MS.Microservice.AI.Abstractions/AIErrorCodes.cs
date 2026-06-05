namespace MS.Microservice.AI.Abstractions;

public static class AIErrorCodes
{
    public const string InvalidConfiguration = "invalid_configuration";
    public const string InvalidRequest = "invalid_request";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderAuthenticationFailed = "provider_auth_failed";
    public const string ProviderTimeout = "provider_timeout";
    public const string RateLimited = "rate_limited";
    public const string ContentFiltered = "content_filtered";
    public const string ResponseInvalid = "response_invalid";
}