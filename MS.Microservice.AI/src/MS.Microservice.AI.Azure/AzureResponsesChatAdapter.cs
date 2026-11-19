using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.Threading;

namespace MS.Microservice.AI.Azure;

internal sealed class AzureResponsesChatAdapter(
    IHttpClientFactory clients,
    IOptions<AIOptions> options,
    AIProviderConcurrency concurrency,
    TimeProvider clock)
{
    private const string Provider = AzureOptions.ProviderName;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask<AIChatResponse> GetResponseAsync(OpenAICompatibleRequestTarget target, AIResolvedModel model,
        AIChatRequest request, CancellationToken cancellationToken)
    {
        var registration = options.Value.Providers[Provider];
        using var lease = await concurrency.GetRequired(Provider).AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var client = clients.CreateClient(AzureOptions.HttpClientName);
        return await AIHttpExecution.ExecuteAsync(Provider, AICapability.Chat, model, request.RequestId, clock, async token =>
        {
            using var response = await SendAsync(client, target, registration, model, request, false, token).ConfigureAwait(false);
            await AIResponseReader.BufferAsync(response, registration, Provider, AICapability.Chat,
                model, request.RequestId, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await AIProviderErrors.CreateAsync(Provider, response, AICapability.Chat, model, request.RequestId, token).ConfigureAwait(false);

            var responseId = AIProviderErrors.GetProviderRequestId(response);
            var json = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            try
            {
                using var document = await JsonDocument.ParseAsync(json, cancellationToken: token).ConfigureAwait(false);
                return ParseResponse(document.RootElement, model, request, responseId);
            }
            catch (JsonException exception)
            {
                throw InvalidResponse(model, request, responseId, "Azure Responses returned invalid JSON.", exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AIChatStreamChunk> StreamAsync(OpenAICompatibleRequestTarget target, AIResolvedModel model,
        AIChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var registration = options.Value.Providers[Provider];
        using var lease = await concurrency.GetRequired(Provider).AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var client = clients.CreateClient(AzureOptions.HttpClientName);
        using var deadline = new CancellationTokenSource(model.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var response = await OpenStreamAsync().ConfigureAwait(false);
        await using var events = ParseEventsAsync(response, registration.MaxStreamLineCharacters, model, request, linked.Token)
            .GetAsyncEnumerator();

        while (true)
        {
            bool next;
            try { next = await events.MoveNextAsync().ConfigureAwait(false); }
            catch (OperationCanceledException exception) when (HasTimedOut()) { throw StreamTimeout(exception); }
            if (!next) yield break;
            yield return events.Current;
        }

        async Task<HttpResponseMessage> OpenStreamAsync()
        {
            try
            {
                return await AIHttpExecution.ExecuteAsync(Provider, AICapability.Chat, model, request.RequestId, clock, async token =>
                {
                    var result = await SendAsync(client, target, registration, model, request, true, token).ConfigureAwait(false);
                    if (result.IsSuccessStatusCode)
                        return result;
                    using (result)
                    {
                        await AIResponseReader.BufferAsync(result, registration, Provider, AICapability.Chat,
                            model, request.RequestId, token).ConfigureAwait(false);
                        throw await AIProviderErrors.CreateAsync(Provider, result, AICapability.Chat, model, request.RequestId, token).ConfigureAwait(false);
                    }
                }, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (HasTimedOut()) { throw StreamTimeout(exception); }
        }

        bool HasTimedOut() => !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested;
        AITimeoutException StreamTimeout(OperationCanceledException exception) => new("Azure Responses stream timed out.",
            provider: Provider, model: model.Model, scenario: model.Scenario, requestId: request.RequestId, innerException: exception);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, OpenAICompatibleRequestTarget target,
        AIProviderRegistrationOptions registration, AIResolvedModel model, AIChatRequest request, bool streaming, CancellationToken token)
    {
        object? format = request.ResponseFormat?.Kind switch
        {
            null or AIChatResponseFormatKind.Text => null,
            AIChatResponseFormatKind.JsonObject => new { type = "json_object" },
            AIChatResponseFormatKind.JsonSchema => new
            {
                type = "json_schema",
                name = request.ResponseFormat.SchemaName,
                schema = request.ResponseFormat.Schema,
                strict = request.ResponseFormat.Strict,
            },
            _ => throw new AIConfigurationException("Unsupported Azure Responses output format."),
        };

        using var message = target.CreateRequest(streaming ? "text/event-stream" : "application/json", registration.Headers);
        message.Content = JsonContent.Create(new
        {
            model = target.Model,
            input = request.Messages.Select(item => new { role = item.Role, content = item.Content }),
            temperature = model.Temperature,
            top_p = model.TopP,
            max_output_tokens = model.MaxOutputTokens,
            store = false,
            stream = streaming,
            text = format is null ? null : new { format },
        }, options: JsonOptions);
        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<AIChatStreamChunk> ParseEventsAsync(HttpResponseMessage response, int limit,
        AIResolvedModel model, AIChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var providerRequestId = AIProviderErrors.GetProviderRequestId(response);
        var resolvedModel = model.Model;
        var emittedText = false;
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        var lines = new AIResponseReader.BoundedLineReader(reader, limit);

        while (true)
        {
            string? data;
            try { data = await ReadSseDataAsync(lines, limit, token).ConfigureAwait(false); }
            catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
            {
                throw AIResponseReader.LimitExceeded(Provider, AICapability.Chat, model, request.RequestId,
                    response, providerRequestId, "streaming event character", exception);
            }
            if (data is null or "[DONE]")
                throw InvalidResponse(model, request, providerRequestId, "Azure Responses stream ended before a completion event.");

            using var document = ParseEvent(data, model, request, providerRequestId);
            var json = document.RootElement;
            var envelope = GetProperty(json, "response");
            providerRequestId ??= GetString(envelope, "id");
            resolvedModel = GetString(envelope, "model") ?? resolvedModel;
            switch (GetString(json, "type"))
            {
                case "response.output_text.delta":
                    var delta = GetString(json, "delta")
                        ?? throw InvalidResponse(model, request, providerRequestId, "Azure Responses returned a malformed text delta.");
                    if (delta.Length > 0)
                    {
                        emittedText = true;
                        yield return CreateChunk(delta);
                    }
                    break;
                case "response.refusal.delta" or "response.refusal.done":
                    throw Refusal(model, request, providerRequestId);
                case "response.failed":
                    throw ResponseFailure(GetProperty(envelope, "error"), model, request, providerRequestId);
                case "error":
                    throw ResponseFailure(json, model, request, providerRequestId);
                case "response.completed" or "response.incomplete":
                    var terminal = ParseResponse(envelope, model, request, providerRequestId);
                    if (!emittedText && terminal.Text.Length > 0)
                        yield return CreateChunk(terminal.Text);
                    yield return CreateChunk(string.Empty) with
                    {
                        IsFinal = true,
                        FinishReason = terminal.FinishReason,
                        Usage = terminal.Usage,
                    };
                    yield break;
            }
        }

        AIChatStreamChunk CreateChunk(string text) => new()
        {
            DeltaText = text,
            Provider = Provider,
            Model = resolvedModel,
            ProviderRequestId = providerRequestId,
        };
    }

    private static async ValueTask<string?> ReadSseDataAsync(AIResponseReader.BoundedLineReader lines, int limit, CancellationToken token)
    {
        StringBuilder? data = null;
        while (await lines.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 && data is not null)
                return data.ToString();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var start = line.Length > 5 && line[5] == ' ' ? 6 : 5;
            if (data is null)
            {
                data = new StringBuilder();
            }
            else
            {
                if (data.Length >= limit - (line.Length - start))
                    throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded,
                        "The Azure Responses streaming event exceeds its configured character limit.");
                data.Append('\n');
            }
            data.Append(line, start, line.Length - start);
        }
        return null;
    }

    private static JsonDocument ParseEvent(string data, AIResolvedModel model, AIChatRequest request, string? responseId)
    {
        try { return JsonDocument.Parse(data); }
        catch (JsonException exception)
        {
            throw InvalidResponse(model, request, responseId, "Azure Responses returned an invalid streaming event.", exception);
        }
    }

    private static AIChatResponse ParseResponse(JsonElement json, AIResolvedModel model, AIChatRequest request, string? responseId)
    {
        responseId ??= GetString(json, "id");
        var status = GetString(json, "status");
        if (status is "failed" or "cancelled")
            throw ResponseFailure(GetProperty(json, "error"), model, request, responseId);

        var incompleteReason = GetString(GetProperty(json, "incomplete_details"), "reason");
        var finish = (status, incompleteReason) switch
        {
            ("completed", _) => "stop",
            ("incomplete", "max_output_tokens") => "length",
            ("incomplete", "content_filter") => throw Refusal(model, request, responseId),
            _ => throw InvalidResponse(model, request, responseId, "Azure Responses returned an unexpected completion status."),
        };

        var output = GetProperty(json, "output");
        if (output.ValueKind != JsonValueKind.Array)
            throw InvalidResponse(model, request, responseId, "Azure Responses omitted its output array.");

        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (GetString(item, "type") != "message")
                continue;
            var content = GetProperty(item, "content");
            if (content.ValueKind != JsonValueKind.Array)
                throw InvalidResponse(model, request, responseId, "Azure Responses returned a malformed output message.");
            foreach (var part in content.EnumerateArray())
            {
                switch (GetString(part, "type"))
                {
                    case "refusal": throw Refusal(model, request, responseId);
                    case "output_text":
                        text.Append(GetString(part, "text")
                            ?? throw InvalidResponse(model, request, responseId, "Azure Responses returned a malformed text output."));
                        break;
                }
            }
        }

        var usage = GetProperty(json, "usage");
        return new AIChatResponse
        {
            Provider = Provider,
            Model = GetString(json, "model") ?? model.Model,
            Text = text.ToString(),
            FinishReason = finish,
            ProviderRequestId = responseId,
            Usage = usage.ValueKind == JsonValueKind.Object ? new AIUsage
            {
                InputTokens = GetInt32(usage, "input_tokens"),
                OutputTokens = GetInt32(usage, "output_tokens"),
                TotalTokens = GetInt32(usage, "total_tokens"),
            } : AIUsage.Zero,
        };
    }

    private static JsonElement GetProperty(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static string? GetString(JsonElement parent, string name)
        => GetProperty(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int GetInt32(JsonElement parent, string name)
        => GetProperty(parent, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var count) ? count : 0;

    private static AIProviderException ResponseFailure(JsonElement error, AIResolvedModel model, AIChatRequest request, string? responseId)
        => GetString(error, "code") switch
        {
            "content_filter" or "image_content_policy_violation" => Refusal(model, request, responseId),
            "rate_limit_exceeded" => new AIRateLimitException("Azure Responses rate limited the request.",
                provider: Provider, model: model.Model, scenario: model.Scenario, requestId: request.RequestId, providerRequestId: responseId),
            "invalid_prompt" => new AIProviderException("Azure Responses rejected the input.", AIErrorCodes.InvalidRequest,
                provider: Provider, model: model.Model, scenario: model.Scenario, requestId: request.RequestId, providerRequestId: responseId),
            _ => new AIProviderException("Azure Responses could not generate a response.", AIErrorCodes.ProviderUnavailable,
                provider: Provider, model: model.Model, scenario: model.Scenario, requestId: request.RequestId, providerRequestId: responseId),
        };

    private static AIContentSafetyException Refusal(AIResolvedModel model, AIChatRequest request, string? responseId)
        => new("Azure Responses refused or filtered the output.", provider: Provider, model: model.Model,
            scenario: model.Scenario, requestId: request.RequestId, providerRequestId: responseId);

    private static AIProviderException InvalidResponse(AIResolvedModel model, AIChatRequest request, string? responseId,
        string message, Exception? inner = null)
        => new(message, AIErrorCodes.ResponseInvalid, provider: Provider, model: model.Model,
            scenario: model.Scenario, requestId: request.RequestId, providerRequestId: responseId, innerException: inner);
}
