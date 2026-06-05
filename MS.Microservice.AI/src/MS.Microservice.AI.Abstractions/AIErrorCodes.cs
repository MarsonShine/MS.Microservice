namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Canonical error codes used across all AI providers and capabilities.
/// These codes enable callers to handle failures programmatically without
/// inspecting provider-specific strings.
/// </summary>
public static class AIErrorCodes
{
    /// <summary>Configuration is missing, incomplete, or invalid.</summary>
    public const string InvalidConfiguration = "invalid_configuration";

    /// <summary>The request payload does not satisfy the capability contract.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>The provider is unreachable or returned a non-retryable 5xx error.</summary>
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>Authentication or authorization with the provider failed (401/403).</summary>
    public const string ProviderAuthenticationFailed = "provider_auth_failed";

    /// <summary>The provider did not respond within the configured or requested timeout.</summary>
    public const string ProviderTimeout = "provider_timeout";

    /// <summary>The provider throttled the request (HTTP 429).</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The request or response was filtered by the provider's content safety policy.</summary>
    public const string ContentFiltered = "content_filtered";

    /// <summary>The provider returned a response that could not be parsed or was empty.</summary>
    public const string ResponseInvalid = "response_invalid";

    /// <summary>The requested capability is not supported by the resolved provider.</summary>
    public const string UnsupportedCapability = "unsupported_capability";
}