using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract class OpenAICompatibleMediaProviderBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly AIProviderRegistrationOptions _providerOptions;
    private readonly SemaphoreSlim _concurrencyGate;

    protected OpenAICompatibleMediaProviderBase(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
        TimeProvider = timeProvider;

        if (!AIOptionsLookup.TryGetProvider(options.Value, Name, out var providerOptions))
        {
            throw new AIConfigurationException($"AI provider '{Name}' is not configured under AI:Providers.");
        }

        _providerOptions = providerOptions;
        _concurrencyGate = new SemaphoreSlim(providerOptions.ConcurrencyLimit, providerOptions.ConcurrencyLimit);
    }

    public abstract string Name { get; }

    protected abstract string HttpClientName { get; }

    protected abstract string DefaultBaseAddress { get; }

    protected TimeProvider TimeProvider { get; }

    /// <summary>
    /// The resolved provider registration options, including <see cref="AIProviderRegistrationOptions.Endpoints"/>.
    /// </summary>
    protected AIProviderRegistrationOptions ProviderOptions => _providerOptions;

    protected Task<TResult> ExecuteAsync<TResult>(
        AICapability capability,
        string operationName,
        AIResolvedModel model,
        string? requestId,
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> parseResponseAsync,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(capability, operationName, model, requestId, createRequest, parseResponseAsync, cancellationToken);
    }

    protected HttpRequestMessage CreateJsonRequest(string relativePath, object payload, string? accept = null)
    {
        var request = CreateRequest(relativePath, accept);
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        return request;
    }

    /// <summary>
    /// Creates a JSON POST request to an absolute endpoint URI (bypassing <see cref="BaseAddress"/>).
    /// Used for provider-specific endpoints such as Qwen multimodal generation.
    /// </summary>
    protected HttpRequestMessage CreateJsonRequest(Uri endpoint, object payload, string? accept = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _providerOptions.ApiKey);

        if (!string.IsNullOrWhiteSpace(accept))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        foreach (var header in _providerOptions.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        return request;
    }

    protected HttpRequestMessage CreateMultipartRequest(string relativePath, MultipartFormDataContent content, string? accept = null)
    {
        var request = CreateRequest(relativePath, accept);
        request.Content = content;
        return request;
    }

    protected ByteArrayContent CreateBinaryContent(AIBinaryContent content)
    {
        var part = new ByteArrayContent(content.Content);
        if (!string.IsNullOrWhiteSpace(content.ContentType))
        {
            part.Headers.ContentType = new MediaTypeHeaderValue(content.ContentType);
        }

        return part;
    }

    protected async Task<AIProviderException> CreateProviderExceptionAsync(
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

        // Fall back to Qwen multimodal top-level error: { code, message, request_id }
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
                message ?? $"AI provider '{Name}' filtered the request or response content.",
                capability,
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: providerRequestId,
                statusCode: statusCode);
        }

        if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new AIRateLimitException(
                message ?? $"AI provider '{Name}' rate limited the request.",
                capability,
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: requestId,
                providerRequestId: providerRequestId,
                statusCode: statusCode,
                retryAfter: retryAfter);
        }

        var errorCode = httpResponse.StatusCode switch
        {
            HttpStatusCode.BadRequest => AIErrorCodes.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AIErrorCodes.ProviderAuthenticationFailed,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AIErrorCodes.ProviderTimeout,
            _ => AIErrorCodes.ProviderUnavailable,
        };

        return new AIProviderException(
            message ?? $"AI provider '{Name}' request failed with status code {(int)httpResponse.StatusCode}.",
            errorCode,
            capability,
            provider: Name,
            model: model.Model,
            scenario: model.Scenario,
            requestId: requestId,
            providerRequestId: providerRequestId,
            statusCode: statusCode,
            isTransient: IsTransient(httpResponse.StatusCode),
            retryAfter: retryAfter,
            innerException: string.IsNullOrWhiteSpace(providerCode)
                ? null
                : new InvalidOperationException($"Provider code: {providerCode}; body: {Truncate(responseText, 512)}"));
    }

    protected static string? GetProviderRequestId(HttpResponseMessage response)
    {
        return GetHeaderValue(response.Headers, "x-request-id")
            ?? GetHeaderValue(response.Headers, "request-id")
            ?? GetHeaderValue(response.Headers, "x-openai-request-id")
            ?? GetHeaderValue(response.Headers, "x-dashscope-request-id");
    }

    protected static AIUsage MapUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement))
        {
            return AIUsage.Zero;
        }

        var promptTokens = TryGetInt32(usageElement, "prompt_tokens");
        var completionTokens = TryGetInt32(usageElement, "completion_tokens");
        var totalTokens = TryGetInt32(usageElement, "total_tokens");

        return new AIUsage
        {
            InputTokens = promptTokens ?? 0,
            OutputTokens = completionTokens ?? 0,
            TotalTokens = totalTokens ?? 0,
        };
    }

    protected static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    protected static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : null;
    }

    protected static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private async Task<TResult> ExecuteCoreAsync<TResult>(
        AICapability capability,
        string operationName,
        AIResolvedModel model,
        string? requestId,
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> parseResponseAsync,
        CancellationToken cancellationToken)
    {
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = TimeProvider.GetTimestamp();
        using var activity = StartActivity(capability, operationName, model, requestId);

        try
        {
            var response = await SendWithRetryAsync(capability, model, requestId, createRequest, parseResponseAsync, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _logger.LogWarning(exception, "AI provider {Provider} {Operation} request failed for model {Model}.", Name, operationName, model.Model);
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "AI provider {Provider} {Operation} request completed in {ElapsedMilliseconds}ms for model {Model}.",
                Name,
                operationName,
                TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds,
                model.Model);
            _concurrencyGate.Release();
        }
    }

    private Activity? StartActivity(AICapability capability, string operationName, AIResolvedModel model, string? requestId)
    {
        var activity = AIActivitySource.Instance.StartActivity($"ai.{operationName}", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("gen_ai.provider", Name);
        activity.SetTag("gen_ai.request.model", model.Model);
        activity.SetTag("gen_ai.operation.name", operationName);
        activity.SetTag("ms.ai.capability", capability.ToString());
        activity.SetTag("ms.ai.scenario", model.Scenario);
        activity.SetTag("ms.ai.request_id", requestId);
        return activity;
    }

    private async Task<TResult> SendWithRetryAsync<TResult>(
        AICapability capability,
        AIResolvedModel model,
        string? requestId,
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> parseResponseAsync,
        CancellationToken cancellationToken)
    {
        var maxAttempts = model.MaxRetryAttempts + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(model.Timeout);

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = createRequest();

            try
            {
                using var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, linkedCts.Token).ConfigureAwait(false);
                return await parseResponseAsync(httpResponse, linkedCts.Token).ConfigureAwait(false);
            }
            catch (AIRateLimitException exception) when (attempt < maxAttempts)
            {
                await DelayForRetryAsync(attempt, exception.RetryAfter, cancellationToken).ConfigureAwait(false);
            }
            catch (AIProviderException exception) when (exception.IsTransient && attempt < maxAttempts)
            {
                await DelayForRetryAsync(attempt, exception.RetryAfter, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await DelayForRetryAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AITimeoutException(
                    $"AI provider '{Name}' timed out after {model.Timeout.TotalSeconds:0.##} seconds.",
                    capability,
                    provider: Name,
                    model: model.Model,
                    scenario: model.Scenario,
                    requestId: requestId,
                    innerException: exception);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await DelayForRetryAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new AIProviderException(
                    $"AI provider '{Name}' request failed: {exception.Message}",
                    AIErrorCodes.ProviderUnavailable,
                    capability,
                    provider: Name,
                    model: model.Model,
                    scenario: model.Scenario,
                    requestId: requestId,
                    isTransient: true,
                    innerException: exception);
            }
        }

        throw new AIProviderException(
            $"AI provider '{Name}' request failed after retry attempts were exhausted.",
            AIErrorCodes.ProviderUnavailable,
            capability,
            provider: Name,
            model: model.Model,
            scenario: model.Scenario,
            requestId: requestId,
            isTransient: true);
    }

    private HttpRequestMessage CreateRequest(string relativePath, string? accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(GetBaseAddress()), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _providerOptions.ApiKey);

        if (!string.IsNullOrWhiteSpace(accept))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        foreach (var header in _providerOptions.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    private string GetBaseAddress()
    {
        var baseAddress = string.IsNullOrWhiteSpace(_providerOptions.BaseAddress)
            ? DefaultBaseAddress
            : _providerOptions.BaseAddress;

        return baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : $"{baseAddress}/";
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

    private static QwenTopLevelError? TryDeserializeTopLevelError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QwenTopLevelError>(responseText, SerializerOptions);
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

    private async Task DelayForRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt - 1), 2_000));
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
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
    /// Top-level error format used by Qwen multimodal generation API:
    /// { "code": "...", "message": "...", "request_id": "..." }
    /// </summary>
    private sealed class QwenTopLevelError
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; init; }
    }
}