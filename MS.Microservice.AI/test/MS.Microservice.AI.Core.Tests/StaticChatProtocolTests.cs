using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core.Tests;

public sealed class StaticChatProtocolTests
{
    [Fact]
    public async Task RequestAndResponsePreserveWebDefaultsWithoutReflection()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var handler = new CaptureHandler("""{"CHOICES":[{"message":{"content":"你好"}}],"usage":{"prompt_tokens":"3","total_tokens":4}}""");
        var provider = new Provider(handler);
        var response = await provider.GetResponseAsync(Model, Request);

        Assert.Equal("你好", response.Text);
        Assert.Equal(3, response.Usage.InputTokens);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.False(payload.RootElement.TryGetProperty("temperature", out _));
        Assert.False(payload.RootElement.TryGetProperty("stream_options", out _));
        Assert.False(payload.RootElement.TryGetProperty("response_format", out _));
        Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("你好", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task NestedSchemaAndStreamingUsageUseGeneratedMetadata()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"value":{"type":"string"}},"additionalProperties":false}""");
        var handler = new CaptureHandler("data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\ndata: {\"choices\":[],\"usage\":{\"total_tokens\":\"5\"}}\n\ndata: [DONE]\n");
        var chunks = new List<AIChatStreamChunk>();
        await foreach (var chunk in new Provider(handler).StreamAsync(Model, Request with
        {
            ResponseFormat = AIChatResponseFormat.JsonSchema("answer", schema.RootElement),
        })) chunks.Add(chunk);

        Assert.Equal("hi", chunks[0].DeltaText);
        Assert.True(chunks[^1].IsFinal);
        Assert.Equal(5, chunks[^1].Usage!.TotalTokens);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.True(payload.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.True(JsonElement.DeepEquals(schema.RootElement,
            payload.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema")));
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\" rejected \",\"code\":\"invalid\"}}", "rejected", null)]
    [InlineData("{\"message\":\" top level \",\"code\":\"invalid\",\"request_id\":\"provider-id\"}", "top level", "provider-id")]
    [InlineData("not json", "AI provider 'Test' request failed with status code 400.", null)]
    public async Task BothErrorShapesAndMalformedFallbackWorkWithoutReflection(string json, string message, string? id)
    {
        var error = await Assert.ThrowsAsync<AIProviderException>(async () =>
            await new Provider(new CaptureHandler(json, HttpStatusCode.BadRequest)).GetResponseAsync(Model, Request));
        Assert.Equal(message, error.Message);
        Assert.Equal(id, error.ProviderRequestId);
        Assert.Equal(AIErrorCodes.InvalidRequest, error.ErrorCode);
    }

    private static readonly AIResolvedModel Model = new()
    {
        Provider = "Test", Model = "model", Scenario = "test", Timeout = TimeSpan.FromSeconds(10), MaxRetryAttempts = 0,
    };
    private static readonly AIChatRequest Request = new() { Messages = [new("user", "你好")] };

    private sealed class Provider(CaptureHandler handler) : OpenAICompatibleChatProviderBase(
        handler, Options.Create(CreateOptions()), TimeProvider.System, NullLogger.Instance)
    {
        public override string Name => "Test";
        protected override string HttpClientName => "test";
        protected override string DefaultBaseAddress => "https://example.test/";
        private static AIOptions CreateOptions()
        {
            var options = new AIOptions();
            options.Providers.Add("Test", new() { ApiKey = "key", ConcurrencyLimit = 1 });
            return options;
        }
    }

    private sealed class CaptureHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler, IHttpClientFactory
    {
        public string? Body { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
