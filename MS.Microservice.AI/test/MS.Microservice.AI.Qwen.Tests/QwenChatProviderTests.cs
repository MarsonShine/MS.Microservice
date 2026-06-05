using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.Qwen;

namespace MS.Microservice.AI.Qwen.Tests;

public sealed class QwenChatProviderTests
{
    [Fact]
    public async Task GetResponseAsync_ShouldUseQwenCompatibleModeEndpoint()
    {
        var handler = new TestHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "id": "qwen-1",
                  "model": "qwen-plus",
                  "choices": [{ "message": { "content": "qwen ok" }, "finish_reason": "stop" }],
                  "usage": { "prompt_tokens": 4, "completion_tokens": 2, "total_tokens": 6 }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateProvider(handler);

        var response = await provider.GetResponseAsync(CreateResolvedModel(), CreateRequest());

        response.Provider.Should().Be(QwenProviderDefaults.ProviderName);
        response.Text.Should().Be("qwen ok");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions");
    }

    [Fact]
    public async Task StreamAsync_ShouldCaptureUsageFromTrailingUsageEnvelope()
    {
        var handler = new TestHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                data: {"id":"qwen-2","model":"qwen-plus","choices":[{"delta":{"content":"千问"},"finish_reason":null}]}

                data: {"id":"qwen-2","model":"qwen-plus","choices":[{"delta":{"content":"流式"},"finish_reason":"stop"}]}

                data: {"id":"qwen-2","model":"qwen-plus","choices":[],"usage":{"prompt_tokens":8,"completion_tokens":3,"total_tokens":11}}

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
        chunks[0].DeltaText.Should().Be("千问");
        chunks[1].DeltaText.Should().Be("流式");
        chunks[2].IsFinal.Should().BeTrue();
        var usage = chunks[2].Usage;
        usage.Should().NotBeNull();
        usage!.InputTokens.Should().Be(8);
        usage.TotalTokens.Should().Be(11);
    }

    private static IAIChatProvider CreateProvider(TestHandler handler)
    {
        var options = new AIOptions();
        options.Providers.Add(QwenProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "qwen-key",
            TimeoutSeconds = 10,
            MaxRetryAttempts = 0,
            ConcurrencyLimit = 2,
        });

        return new QwenChatProvider(
            new TestHttpClientFactory(handler),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<QwenChatProvider>.Instance);
    }

    private static AIResolvedModel CreateResolvedModel() => new()
    {
        Provider = QwenProviderDefaults.ProviderName,
        Model = "qwen-plus",
        Scenario = "Default",
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetryAttempts = 0,
    };

    private static AIChatRequest CreateRequest() => new()
    {
        Messages = [new AIChatMessage("user", "hello")],
        RequestId = "req-qwen-1",
    };

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(_response);
        }
    }
}