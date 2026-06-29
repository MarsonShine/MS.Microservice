using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.OpenAI;

namespace MS.Microservice.AI.OpenAI.Tests;

public sealed class OpenAIChatProviderTests
{
    [Fact]
    public async Task GetResponseAsync_ShouldSendChatCompletionRequest_AndParseUsage()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "id": "chatcmpl-1",
                  "model": "gpt-4.1-mini",
                  "choices": [
                    {
                      "message": { "content": "hello world" },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 12,
                    "completion_tokens": 8,
                    "total_tokens": 20
                  }
                }
                """));

        var provider = CreateProvider(handler);

        var response = await provider.GetResponseAsync(CreateResolvedModel(), CreateRequest());

        response.Text.Should().Be("hello world");
        response.Usage.InputTokens.Should().Be(12);
        response.Usage.OutputTokens.Should().Be(8);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("openai-key");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("\"model\":\"gpt-4.1-mini\"");
        body.Should().Contain("\"stream\":false");
    }

    [Fact]
    public async Task GetResponseAsync_ShouldRetryOnRateLimit_AndEventuallySucceed()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.TooManyRequests,
                """
                {
                  "error": {
                    "message": "Too many requests",
                    "code": "rate_limit_reached"
                  }
                }
                """,
                retryAfterSeconds: 0),
            _ => CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "id": "chatcmpl-2",
                  "model": "gpt-4.1-mini",
                  "choices": [
                    {
                      "message": { "content": "retry success" },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 10,
                    "completion_tokens": 3,
                    "total_tokens": 13
                  }
                }
                """));

        var provider = CreateProvider(handler);

        var response = await provider.GetResponseAsync(CreateResolvedModel(), CreateRequest());

        response.Text.Should().Be("retry success");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetResponseAsync_ShouldWrapAuthenticationFailures()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateJsonResponse(
                HttpStatusCode.Unauthorized,
                """
                {
                  "error": {
                    "message": "Incorrect API key provided.",
                    "code": "invalid_api_key"
                  }
                }
                """));

        var provider = CreateProvider(handler);

        Func<Task> action = async () => await provider.GetResponseAsync(CreateResolvedModel(), CreateRequest());

        var exception = await action.Should().ThrowAsync<AIProviderException>();
        exception.Which.ErrorCode.Should().Be(AIErrorCodes.ProviderAuthenticationFailed);
        exception.Which.Provider.Should().Be(OpenAIProviderDefaults.ProviderName);
    }

    [Fact]
    public async Task StreamAsync_ShouldParseSseDeltaEvents_AndFinalUsageChunk()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => CreateSseResponse(
                """
                data: {"id":"chatcmpl-3","model":"gpt-4.1-mini","choices":[{"delta":{"content":"hello "},"finish_reason":null}]}

                data: {"id":"chatcmpl-3","model":"gpt-4.1-mini","choices":[{"delta":{"content":"stream"},"finish_reason":"stop"}]}

                data: {"id":"chatcmpl-3","model":"gpt-4.1-mini","choices":[],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7}}

                data: [DONE]
                """));

        var provider = CreateProvider(handler);
        var chunks = new List<AIChatStreamChunk>();

        await foreach (var chunk in provider.StreamAsync(CreateResolvedModel(), CreateRequest()))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(3);
        chunks[0].DeltaText.Should().Be("hello ");
        chunks[1].DeltaText.Should().Be("stream");
        chunks[2].IsFinal.Should().BeTrue();
        chunks[2].Usage!.TotalTokens.Should().Be(7);
        handler.Requests[0].Headers.Accept.Should().Contain(header => header.MediaType == "text/event-stream");
    }

    [Fact]
    public void Validate_WhenOpenAIIsNotReferenced_ShouldSucceed()
    {
        var result = new OpenAIOptionsValidator().Validate(null, new AIOptions
        {
            DefaultProvider = "Qwen",
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenOpenAIUsesFallbackBaseAddress_ShouldSucceed()
    {
        var options = new AIOptions
        {
            DefaultProvider = OpenAIProviderDefaults.ProviderName,
        };
        options.Providers.Add(OpenAIProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "openai-key",
        });

        var result = new OpenAIOptionsValidator().Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenOpenAIIsReferencedButInvalid_ShouldReturnFailures()
    {
        var options = new AIOptions
        {
            DefaultProvider = OpenAIProviderDefaults.ProviderName,
        };
        options.Providers.Add(OpenAIProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            Enabled = false,
            ApiKey = string.Empty,
            BaseAddress = "not-a-uri",
        });

        var result = new OpenAIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain($"AI:Providers:{OpenAIProviderDefaults.ProviderName}:Enabled must be true when the provider is in use.");
        result.Failures.Should().Contain($"AI:Providers:{OpenAIProviderDefaults.ProviderName}:ApiKey is required.");
        result.Failures.Should().Contain($"AI:Providers:{OpenAIProviderDefaults.ProviderName}:BaseAddress must be a valid absolute URI.");
    }

    private static IAIChatProvider CreateProvider(SequenceHttpMessageHandler handler)
    {
        var options = new AIOptions();
        options.Providers.Add(OpenAIProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "openai-key",
            BaseAddress = OpenAIProviderDefaults.DefaultBaseAddress,
            TimeoutSeconds = 10,
            MaxRetryAttempts = 1,
            ConcurrencyLimit = 2,
        });

        return new OpenAIChatProvider(
            new TestHttpClientFactory(handler),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<OpenAIChatProvider>.Instance);
    }

    private static AIResolvedModel CreateResolvedModel()
    {
        return new AIResolvedModel
        {
            Provider = OpenAIProviderDefaults.ProviderName,
            Model = "gpt-4.1-mini",
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            MaxRetryAttempts = 1,
            Temperature = 0.1,
        };
    }

    private static AIChatRequest CreateRequest()
    {
        return new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
            RequestId = "req-openai-1",
        };
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string body, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (retryAfterSeconds is not null)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds.Value));
        }

        response.Headers.TryAddWithoutValidation("x-request-id", "openai-request-id");
        return response;
    }

    private static HttpResponseMessage CreateSseResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };
        response.Headers.TryAddWithoutValidation("x-request-id", "openai-stream-id");
        return response;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler, disposeHandler: false);
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public SequenceHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            var responseFactory = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("No response configured for the test request.");
            return Task.FromResult(responseFactory(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
            }

            return clone;
        }
    }
}
