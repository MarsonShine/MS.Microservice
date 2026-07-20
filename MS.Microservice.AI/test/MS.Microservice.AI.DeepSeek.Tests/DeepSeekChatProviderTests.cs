using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

namespace MS.Microservice.AI.DeepSeek.Tests;

public sealed class DeepSeekChatProviderTests
{
    [Fact]
    public async Task GetResponseAsync_ShouldUseDeepSeekDefaultEndpoint_WhenBaseAddressIsMissing()
    {
        var handler = new TestHandler(CreateJsonResponse(HttpStatusCode.OK, """
            {
              "id": "deepseek-1",
              "model": "deepseek-v4-pro",
              "choices": [{ "message": { "content": "deepseek ok" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 6, "completion_tokens": 2, "total_tokens": 8 }
            }
            """));
        var provider = CreateProvider(handler);

        var response = await provider.GetResponseAsync(CreateResolvedModel(), CreateRequest());

        response.Provider.Should().Be(DeepSeekProviderDefaults.ProviderName);
        response.Text.Should().Be("deepseek ok");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.deepseek.com/chat/completions");
    }

    [Fact]
    public async Task StreamAsync_ShouldEmitFinalChunk_WhenProviderEndsWithDoneMarkerOnly()
    {
        var handler = new TestHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                data: {"id":"deepseek-2","model":"deepseek-v4-pro","choices":[{"delta":{"content":"deep "},"finish_reason":null}]}

                data: {"id":"deepseek-2","model":"deepseek-v4-pro","choices":[{"delta":{"content":"stream"},"finish_reason":"stop"}]}

                data: [DONE]
                """,
                Encoding.UTF8,
                "text/event-stream"),
        });
        var provider = CreateProvider(handler);
        var chunks = new List<AIChatStreamChunk>();

        await foreach (var chunk in provider.StreamAsync(CreateResolvedModel(), CreateRequest()))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(3);
        chunks[0].DeltaText.Should().Be("deep ");
        chunks[1].DeltaText.Should().Be("stream");
        chunks[2].IsFinal.Should().BeTrue();
        chunks[2].Usage.Should().BeNull();
    }

    [Fact]
    public async Task GetResponseAsync_ShouldSendJsonObjectResponseFormat()
    {
        var handler = new TestHandler(CreateJsonResponse(HttpStatusCode.OK, """
            {
              "id": "deepseek-json",
              "model": "deepseek-v4-pro",
              "choices": [{ "message": { "content": "{}" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
            }
            """));
        var provider = CreateProvider(handler);
        var request = CreateRequest() with { ResponseFormat = AIChatResponseFormat.JsonObject };

        await provider.GetResponseAsync(CreateResolvedModel(), request);

        handler.RequestBodies.Should().ContainSingle()
            .Which.Should().Contain("\"response_format\":{\"type\":\"json_object\"");
    }

    private static IAIChatProvider CreateProvider(TestHandler handler)
    {
        var options = new AIOptions();
        options.Providers.Add(DeepSeekProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "deepseek-key",
            TimeoutSeconds = 10,
            MaxRetryAttempts = 0,
            ConcurrencyLimit = 2,
        });

        return new DeepSeekChatProvider(
            new TestHttpClientFactory(handler),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<DeepSeekChatProvider>.Instance);
    }

    private static AIResolvedModel CreateResolvedModel() => new()
    {
        Provider = DeepSeekProviderDefaults.ProviderName,
        Model = "deepseek-v4-pro",
        Scenario = "Default",
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetryAttempts = 0,
    };

    private static AIChatRequest CreateRequest() => new()
    {
        Messages = [new AIChatMessage("user", "hello")],
        RequestId = "req-deepseek-1",
    };

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public TestHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            RequestBodies.Add(
                request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            return Task.FromResult(_response);
        }
    }
}
