using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal static class AIProviderErrors
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    internal static async Task<AIProviderException> CreateAsync(string name,
        HttpResponseMessage httpResponse,
        AICapability capability,
        AIResolvedModel model,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var providerRequestId = GetProviderRequestId(httpResponse);
        var retryAfter = GetRetryAfter(httpResponse.Headers.RetryAfter);
        var statusCode = (int)httpResponse.StatusCode;

        // Try OpenAI-compatible error envelope first: { error: { message, type, code } }
        var envelope = TryDeserializeError(responseText);
        var message = envelope?.Error?.Message?.Trim();
        var providerCode = envelope?.Error?.Code?.Trim() ?? envelope?.Error?.Type?.Trim();

        // Fall back to provider top-level error: { code, message, request_id }
        if (string.IsNullOrWhiteSpace(message))
        {
            var topLevel = TryDeserializeTopLevelError(responseText);
            if (topLevel is not null)
            {
                message = topLevel.Message?.Trim();
                providerCode = topLevel.Code?.Trim();
                providerRequestId ??= topLevel.RequestId?.Trim();
            }
        }

        if (IsContentSafetyError(providerCode, message))
        {
            return new AIContentSafetyException(
                message ?? $"AI provider '{name}' filtered the request or response content.",
                capability,
                provider: name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: providerRequestId,
                statusCode: statusCode);
        }

        if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new AIRateLimitException(
                message ?? $"AI provider '{name}' rate limited the request.",
                capability,
                provider: name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: providerRequestId,
                statusCode: statusCode,
                retryAfter: retryAfter);
        }

        var errorCode = capability == AICapability.Chat && IsUnsupportedResponseFormat(httpResponse.StatusCode, providerCode, message) ? AIErrorCodes.UnsupportedResponseFormat : httpResponse.StatusCode switch
        {
            HttpStatusCode.BadRequest => AIErrorCodes.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AIErrorCodes.ProviderAuthenticationFailed,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AIErrorCodes.ProviderTimeout,
            _ => AIErrorCodes.ProviderUnavailable,
        };

        return new AIProviderException(
            message ?? $"AI provider '{name}' request failed with status code {(int)httpResponse.StatusCode}.",
            errorCode,
            capability,
            provider: name,
            model: model.Model,
            scenario: model.Scenario,
            requestId: requestId,
            providerRequestId: providerRequestId,
            statusCode: statusCode,
            isTransient: IsTransient(httpResponse.StatusCode),
            retryAfter: retryAfter,
            innerException: string.IsNullOrWhiteSpace(providerCode)
                ? null
                : new InvalidOperationException($"Provider code: {providerCode}"));
    }

    internal static string? GetProviderRequestId(HttpResponseMessage response)
    {
        return GetHeaderValue(response.Headers, "x-request-id")
            ?? GetHeaderValue(response.Headers, "request-id")
            ?? GetHeaderValue(response.Headers, "x-openai-request-id")
            ?? GetHeaderValue(response.Headers, "x-dashscope-request-id");
    }

    private static bool IsUnsupportedResponseFormat(
        HttpStatusCode statusCode,
        string? providerCode,
        string? message)
    {
        if (statusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var value = $"{providerCode} {message}";
        var mentionsFormat = value.Contains("response_format", StringComparison.OrdinalIgnoreCase)
            || value.Contains("json_schema", StringComparison.OrdinalIgnoreCase)
            || value.Contains("json object", StringComparison.OrdinalIgnoreCase);
        var unsupported = value.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || value.Contains("not support", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unknown", StringComparison.OrdinalIgnoreCase)
            || value.Contains("invalid type", StringComparison.OrdinalIgnoreCase);
        return mentionsFormat && unsupported;
    }

    private static OpenAICompatibleErrorEnvelope? TryDeserializeError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OpenAICompatibleErrorEnvelope>(responseText, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TopLevelProviderError? TryDeserializeTopLevelError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TopLevelProviderError>(responseText, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsContentSafetyError(string? providerCode, string? message)
    {
        var value = $"{providerCode} {message}";
        return value.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
            || value.Contains("content policy", StringComparison.OrdinalIgnoreCase)
            || value.Contains("safety", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout
            || (int)statusCode >= 500;
    }

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter?.Date is not null)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static string? GetHeaderValue(HttpHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private sealed class OpenAICompatibleErrorEnvelope
    {
        [JsonPropertyName("error")]
        public OpenAICompatibleError? Error { get; init; }
    }

    private sealed class OpenAICompatibleError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }

    /// <summary>
    /// Provider top-level error format: { "code": "...", "message": "...", "request_id": "..." }.
    /// Used as a fallback when the OpenAI-compatible error envelope is absent.
    /// </summary>
    private sealed class TopLevelProviderError
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; init; }
    }
}