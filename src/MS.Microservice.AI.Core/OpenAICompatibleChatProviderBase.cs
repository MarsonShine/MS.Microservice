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
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            _logger.LogWarning("AI provider {Provider} chat request failed for model {Model}: {FailureType}.", Name, model.Model, exception.GetType().Name);
            throw;
        }
        finally
        {
            ChatCompleted(_logger, Name, TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds, model.Model);
            _concurrencyGate.Release();
        }
    }

    public async IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = TimeProvider.GetTimestamp();
        using var activity = StartActivity(model, request, isStreaming: true);
        using var timeout = new CancellationTokenSource(model.Timeout, TimeProvider);
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        StreamingResponse? stream = null;
        var completed = false;
        try
        {
            try
            {
                stream = await SendWithRetryAsync(model, request, true, async (client, message, token) =>
                {
                    var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    return await EnsureStreamingResponseAsync(response, model, request, token).ConfigureAwait(false);
                }, streamCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw StreamTimeout(model, request, exception);
            }
            await using var parser = ParseStreamAsync(stream, model, request, streamCancellation.Token).GetAsyncEnumerator();
            while (true)
            {
                bool next;
                try { next = await parser.MoveNextAsync().ConfigureAwait(false); }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    throw StreamTimeout(model, request, exception);
                }
                if (!next) break;
                yield return parser.Current;
            }
            completed = true;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            stream?.Response.Dispose();
            if (!completed) activity?.SetStatus(ActivityStatusCode.Error);
            ChatCompleted(_logger, Name, TimeProvider.GetElapsedTime(startedAt).TotalMilliseconds, model.Model);
            _concurrencyGate.Release();
        }
    }

    private AITimeoutException StreamTimeout(AIResolvedModel model, AIChatRequest request, Exception exception)
        => new($"AI provider '{Name}' stream timed out.", provider: Name, model: model.Model,
            scenario: model.Scenario, requestId: request.RequestId, innerException: exception);
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

    private Task<TResult> SendWithRetryAsync<TResult>(AIResolvedModel model, AIChatRequest request, bool isStreaming,
        Func<HttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> sendAsync, CancellationToken cancellationToken)
        => AIHttpExecution.ExecuteAsync(Name, AICapability.Chat, model, request.RequestId, TimeProvider, async token =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var message = CreateHttpRequestMessage(model, request, isStreaming);
            return await sendAsync(client, message, token).ConfigureAwait(false);
        }, cancellationToken);
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
            ResponseFormat = MapResponseFormat(request.ResponseFormat),
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

    private static OpenAICompatibleResponseFormat? MapResponseFormat(AIChatResponseFormat? format)
    {
        return format?.Kind switch
        {
            null or AIChatResponseFormatKind.Text => null,
            AIChatResponseFormatKind.JsonObject => new OpenAICompatibleResponseFormat
            {
                Type = "json_object",
            },
            AIChatResponseFormatKind.JsonSchema => new OpenAICompatibleResponseFormat
            {
                Type = "json_schema",
                JsonSchema = new OpenAICompatibleJsonSchema
                {
                    Name = format.SchemaName!,
                    Strict = format.Strict,
                    Schema = format.Schema!.Value,
                },
            },
            _ => throw new AIConfigurationException($"Unsupported chat response format '{format.Kind}'."),
        };
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

        var sawDone = false;
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
                sawDone = true;
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

        if (!sawDone && string.IsNullOrWhiteSpace(finishReason))
            throw new AIProviderException($"AI provider '{Name}' stream ended before a completion marker.",
                AIErrorCodes.ResponseInvalid, provider: Name, model: model.Model, scenario: model.Scenario,
                requestId: request.RequestId, providerRequestId: providerRequestId);

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

    private Task<AIProviderException> CreateProviderExceptionAsync(HttpResponseMessage response, AIResolvedModel model,
        AIChatRequest request, CancellationToken cancellationToken)
        => AIProviderErrors.CreateAsync(Name, response, AICapability.Chat, model, request.RequestId, cancellationToken);
    private string GetBaseAddress()
    {
        var baseAddress = string.IsNullOrWhiteSpace(_providerOptions.BaseAddress)
            ? DefaultBaseAddress
            : _providerOptions.BaseAddress;

        return baseAddress.EndsWith('/') ? baseAddress : $"{baseAddress}/";
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

    private static string? GetProviderRequestId(HttpResponseMessage response)
        => AIProviderErrors.GetProviderRequestId(response);
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

        [JsonPropertyName("response_format")]
        public OpenAICompatibleResponseFormat? ResponseFormat { get; init; }
    }

    protected sealed class OpenAICompatibleResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("json_schema")]
        public OpenAICompatibleJsonSchema? JsonSchema { get; init; }
    }

    protected sealed class OpenAICompatibleJsonSchema
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("strict")]
        public bool Strict { get; init; }

        [JsonPropertyName("schema")]
        public JsonElement Schema { get; init; }
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

}
