using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

internal abstract partial class OpenAICompatibleChatProviderBase : IAIChatProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly AIProviderRegistrationOptions _providerOptions;
    private readonly SemaphoreSlim _concurrencyGate;

    protected OpenAICompatibleChatProviderBase(
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

    protected virtual string RelativeChatCompletionsPath => "chat/completions";

    public async ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = TimeProvider.GetTimestamp();
        using var activity = StartActivity(model, request, isStreaming: false);

        try
        {
            var response = await SendWithRetryAsync(
                    model,
                    request,
                    isStreaming: false,
                    async (httpClient, httpRequest, requestCancellationToken) =>
                    {
                        using var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, requestCancellationToken).ConfigureAwait(false);
                        return await ParseChatCompletionResponseAsync(httpResponse, model, request, requestCancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _logger.LogWarning(exception, "AI provider {Provider} chat request failed for model {Model}.", Name, model.Model);
            throw;
        }
        finally
        {
            ChatCompleted(_logger, Name, TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds, model.Model);
            _concurrencyGate.Release();
        }
    }

    public async IAsyncEnumerable<AIChatStreamChunk> StreamAsync(
        AIResolvedModel model,
        AIChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = TimeProvider.GetTimestamp();
        var activity = StartActivity(model, request, isStreaming: true);
        StreamingResponse stream;

        try
        {
            stream = await SendWithRetryAsync(
                model,
                request,
                isStreaming: true,
                async (httpClient, httpRequest, requestCancellationToken) =>
                {
                    var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, requestCancellationToken).ConfigureAwait(false);
                    return await EnsureStreamingResponseAsync(httpResponse, model, request, requestCancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _logger.LogWarning(exception, "AI provider {Provider} chat stream failed for model {Model}.", Name, model.Model);
            ChatCompleted(_logger, Name, TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds, model.Model);
            activity?.Dispose();
            _concurrencyGate.Release();
            throw;
        }

        var completed = false;

        try
        {
            await foreach (var chunk in ParseStreamAsync(stream, model, request, cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
            }

            completed = true;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            if (!completed)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            ChatCompleted(_logger, Name, TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds, model.Model);
            activity?.Dispose();
            _concurrencyGate.Release();
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "AI provider {Provider} chat stream completed in {ElapsedMilliseconds}ms for model {Model}.")]
    private static partial void ChatCompleted(ILogger logger, string provider, double elapsedMilliseconds, string model);

    protected virtual void CustomizeRequest(OpenAICompatibleChatCompletionRequest payload, AIChatRequest request, AIResolvedModel model)
    {
    }

    private Activity? StartActivity(AIResolvedModel model, AIChatRequest request, bool isStreaming)
    {
        var activity = AIActivitySource.Instance.StartActivity("ai.chat", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("gen_ai.provider", Name);
        activity.SetTag("gen_ai.request.model", model.Model);
        activity.SetTag("gen_ai.operation.name", "chat");
        activity.SetTag("ms.ai.scenario", model.Scenario);
        activity.SetTag("ms.ai.streaming", isStreaming);
        activity.SetTag("ms.ai.request_id", request.RequestId);
        return activity;
    }

    private async Task<TResult> SendWithRetryAsync<TResult>(
        AIResolvedModel model,
        AIChatRequest request,
        bool isStreaming,
        Func<HttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> sendAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);

        var maxAttempts = model.MaxRetryAttempts + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(model.Timeout);

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = CreateHttpRequestMessage(model, request, isStreaming);

            try
            {
                return await sendAsync(httpClient, httpRequest, linkedCts.Token).ConfigureAwait(false);
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
                    provider: Name,
                    model: model.Model,
                    scenario: model.Scenario,
                    requestId: request.RequestId,
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
                    provider: Name,
                    model: model.Model,
                    scenario: model.Scenario,
                    requestId: request.RequestId,
                    isTransient: true,
                    innerException: exception);
            }
        }

        throw new AIProviderException(
            $"AI provider '{Name}' request failed after retry attempts were exhausted.",
            AIErrorCodes.ProviderUnavailable,
            provider: Name,
            model: model.Model,
            scenario: model.Scenario,
            requestId: request.RequestId,
            isTransient: true);
    }

    private HttpRequestMessage CreateHttpRequestMessage(AIResolvedModel model, AIChatRequest request, bool isStreaming)
    {
        var payload = new OpenAICompatibleChatCompletionRequest
        {
            Model = model.Model,
            Messages = request.Messages.Select(message => new OpenAICompatibleChatMessage
            {
                Role = message.Role,
                Content = message.Content,
            }).ToList(),
            Temperature = model.Temperature,
            TopP = model.TopP,
            MaxTokens = model.MaxOutputTokens,
            Stream = isStreaming,
            StreamOptions = isStreaming ? new OpenAICompatibleStreamOptions { IncludeUsage = true } : null,
        };

        CustomizeRequest(payload, request, model);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(GetBaseAddress()), RelativeChatCompletionsPath));
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _providerOptions.ApiKey);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(isStreaming ? "text/event-stream" : "application/json"));

        foreach (var header in _providerOptions.Headers)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        requestMessage.Content = JsonContent.Create(payload, options: SerializerOptions);
        return requestMessage;
    }

    private async Task<AIChatResponse> ParseChatCompletionResponseAsync(
        HttpResponseMessage httpResponse,
        AIResolvedModel model,
        AIChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(httpResponse, model, request, cancellationToken).ConfigureAwait(false);
        }

        var envelope = await httpResponse.Content.ReadFromJsonAsync<OpenAICompatibleChatCompletionEnvelope>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (envelope is null || envelope.Choices.Count == 0)
        {
            throw new AIProviderException(
                $"AI provider '{Name}' returned an empty chat response.",
                AIErrorCodes.ResponseInvalid,
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: request.RequestId,
                providerRequestId: GetProviderRequestId(httpResponse));
        }

        var choice = envelope.Choices[0];
        return new AIChatResponse
        {
            Provider = Name,
            Model = envelope.Model ?? model.Model,
            Text = choice.Message?.Content ?? string.Empty,
            FinishReason = choice.FinishReason,
            Usage = MapUsage(envelope.Usage),
            ProviderRequestId = GetProviderRequestId(httpResponse) ?? envelope.Id,
        };
    }

    private async Task<StreamingResponse> EnsureStreamingResponseAsync(
        HttpResponseMessage httpResponse,
        AIResolvedModel model,
        AIChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            using (httpResponse)
            {
                throw await CreateProviderExceptionAsync(httpResponse, model, request, cancellationToken).ConfigureAwait(false);
            }
        }

        return new StreamingResponse(httpResponse, GetProviderRequestId(httpResponse));
    }

    private async IAsyncEnumerable<AIChatStreamChunk> ParseStreamAsync(
        StreamingResponse streamingResponse,
        AIResolvedModel model,
        AIChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var httpResponse = streamingResponse.Response;
        await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream);

        AIUsage? usage = null;
        string? finishReason = null;
        var providerRequestId = streamingResponse.ProviderRequestId;
        var resolvedModelName = model.Model;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            var envelope = JsonSerializer.Deserialize<OpenAICompatibleChatCompletionEnvelope>(payload, SerializerOptions)
                ?? throw new AIProviderException(
                    $"AI provider '{Name}' returned an invalid streaming payload.",
                    AIErrorCodes.ResponseInvalid,
                    provider: Name,
                    model: model.Model,
                    scenario: model.Scenario,
                    requestId: request.RequestId,
                    providerRequestId: providerRequestId);
            providerRequestId ??= envelope.Id;
            resolvedModelName = envelope.Model ?? resolvedModelName;
            usage = envelope.Usage is null ? usage : MapUsage(envelope.Usage);

            if (envelope.Choices.Count == 0)
            {
                continue;
            }

            var choice = envelope.Choices[0];
            finishReason ??= choice.FinishReason;

            var deltaText = choice.Delta?.Content ?? string.Empty;
            if (deltaText.Length == 0)
            {
                continue;
            }

            yield return new AIChatStreamChunk
            {
                DeltaText = deltaText,
                Provider = Name,
                Model = resolvedModelName,
                ProviderRequestId = providerRequestId,
            };
        }

        yield return new AIChatStreamChunk
        {
            IsFinal = true,
            FinishReason = finishReason ?? "stop",
            Usage = usage,
            Provider = Name,
            Model = resolvedModelName,
            ProviderRequestId = providerRequestId,
        };
    }

    private async Task<AIProviderException> CreateProviderExceptionAsync(
        HttpResponseMessage httpResponse,
        AIResolvedModel model,
        AIChatRequest request,
        CancellationToken cancellationToken)
    {
        var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var envelope = TryDeserializeError(responseText);
        var providerRequestId = GetProviderRequestId(httpResponse);
        var retryAfter = GetRetryAfter(httpResponse.Headers.RetryAfter);
        var message = envelope?.Error?.Message?.Trim();
        var providerCode = envelope?.Error?.Code?.Trim() ?? envelope?.Error?.Type?.Trim();
        var statusCode = (int)httpResponse.StatusCode;

        if (IsContentSafetyError(providerCode, message))
        {
            return new AIContentSafetyException(
                message ?? $"AI provider '{Name}' filtered the request or response content.",
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: request.RequestId,
                providerRequestId: providerRequestId,
                statusCode: statusCode);
        }

        if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new AIRateLimitException(
                message ?? $"AI provider '{Name}' rate limited the request.",
                provider: Name,
                model: model.Model,
                scenario: model.Scenario,
                requestId: request.RequestId,
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
            provider: Name,
            model: model.Model,
            scenario: model.Scenario,
            requestId: request.RequestId,
            providerRequestId: providerRequestId,
            statusCode: statusCode,
            isTransient: IsTransient(httpResponse.StatusCode),
            retryAfter: retryAfter,
            innerException: string.IsNullOrWhiteSpace(providerCode)
                ? null
                : new InvalidOperationException($"Provider code: {providerCode}; body: {Truncate(responseText, 512)}"));
    }

    private string GetBaseAddress()
    {
        var baseAddress = string.IsNullOrWhiteSpace(_providerOptions.BaseAddress)
            ? DefaultBaseAddress
            : _providerOptions.BaseAddress;

        return baseAddress.EndsWith('/') ? baseAddress : $"{baseAddress}/";
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

    private static AIUsage MapUsage(OpenAICompatibleUsage? usage)
    {
        return usage is null
            ? AIUsage.Zero
            : new AIUsage
            {
                InputTokens = usage.PromptTokens,
                OutputTokens = usage.CompletionTokens,
                TotalTokens = usage.TotalTokens,
            };
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

    private static string? GetProviderRequestId(HttpResponseMessage response)
    {
        return GetHeaderValue(response.Headers, "x-request-id")
            ?? GetHeaderValue(response.Headers, "request-id")
            ?? GetHeaderValue(response.Headers, "x-openai-request-id")
            ?? GetHeaderValue(response.Headers, "x-dashscope-request-id");
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

    private sealed record StreamingResponse(HttpResponseMessage Response, string? ProviderRequestId);

    protected sealed class OpenAICompatibleChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenAICompatibleChatMessage> Messages { get; init; } = [];

        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; init; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("stream_options")]
        public OpenAICompatibleStreamOptions? StreamOptions { get; init; }
    }

    protected sealed class OpenAICompatibleChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;
    }

    protected sealed class OpenAICompatibleStreamOptions
    {
        [JsonPropertyName("include_usage")]
        public bool IncludeUsage { get; init; }
    }

    private sealed class OpenAICompatibleChatCompletionEnvelope
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public List<OpenAICompatibleChoice> Choices { get; init; } = [];

        [JsonPropertyName("usage")]
        public OpenAICompatibleUsage? Usage { get; init; }
    }

    private sealed class OpenAICompatibleChoice
    {
        [JsonPropertyName("message")]
        public OpenAICompatibleResponseMessage? Message { get; init; }

        [JsonPropertyName("delta")]
        public OpenAICompatibleResponseDelta? Delta { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class OpenAICompatibleResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class OpenAICompatibleResponseDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class OpenAICompatibleUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; init; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; init; }
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
}