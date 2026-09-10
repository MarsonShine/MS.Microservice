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
    /// Used for provider-specific endpoints that require a different base URL.
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

    protected Task<AIProviderException> CreateProviderExceptionAsync(HttpResponseMessage response, AICapability capability,
        AIResolvedModel model, string? requestId, CancellationToken cancellationToken)
        => AIProviderErrors.CreateAsync(Name, response, capability, model, requestId, cancellationToken);

    protected static string? GetProviderRequestId(HttpResponseMessage response)
        => AIProviderErrors.GetProviderRequestId(response);
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
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            _logger.LogWarning( "AI provider {Provider} {Operation} request failed for model {Model}.", Name, operationName, model.Model);
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

    private Task<TResult> SendWithRetryAsync<TResult>(AICapability capability, AIResolvedModel model, string? requestId,
        Func<HttpRequestMessage> createRequest, Func<HttpResponseMessage, CancellationToken, Task<TResult>> parseResponseAsync,
        CancellationToken cancellationToken)
        => AIHttpExecution.ExecuteAsync(Name, capability, model, requestId, TimeProvider, async token =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = createRequest();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            return await parseResponseAsync(response, token).ConfigureAwait(false);
        }, cancellationToken);
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

}
