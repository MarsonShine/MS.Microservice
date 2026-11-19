using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;
using Xunit;

namespace MS.Microservice.AI.Azure.Tests;

public sealed class AzureResponsesChatTests
{
    private const string ModelName = "reasoning";
    private const string Chat = "AzureCloud:CHAT";

    [Fact]
    public async Task ResponsesUsesTheAzureDeploymentAndMapsStructuredInputAndOutput()
    {
        using var data = JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}""");
        using var handler = new RecordingHandler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https://responses.azure.invalid/openai/v1/responses", message.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", Assert.Single(message.Headers.GetValues("api-key")));
            Assert.Null(message.Headers.Authorization);
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            var request = body.RootElement;
            Assert.Equal("responses-deployment", request.GetProperty("model").GetString());
            Assert.Equal("system", request.GetProperty("input")[0].GetProperty("role").GetString());
            Assert.Equal("Give a short answer", request.GetProperty("input")[0].GetProperty("content").GetString());
            Assert.Equal("Hello", request.GetProperty("input")[1].GetProperty("content").GetString());
            Assert.Equal(128, request.GetProperty("max_output_tokens").GetInt32());
            Assert.False(request.GetProperty("store").GetBoolean());
            Assert.False(request.TryGetProperty("messages", out _));
            Assert.False(request.TryGetProperty("max_completion_tokens", out _));
            var format = request.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.Equal("response", format.GetProperty("name").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Equal("object", format.GetProperty("schema").GetProperty("type").GetString());
            var result = JsonResponse("""
                {
                  "id": "response-1", "model": "actual-model", "status": "completed",
                  "output": [
                    {"type":"reasoning"},
                    {"type":"message","content":[{"type":"output_text","text":"{\"answer\":"},{"type":"output_text","text":"\"ok\"}"}]}
                  ],
                  "usage": {"input_tokens": 8, "output_tokens": 4, "total_tokens": 12}
                }
                """);
            result.Headers.Add("apim-request-id", "apim-1");
            return result;
        });
        using var services = BuildServices(handler);

        var response = await services.GetRequiredService<IAIChatClient>().GetResponseAsync(new AIChatRequest
        {
            Provider = "Azure",
            Model = ModelName,
            Messages = [new AIChatMessage("system", "Give a short answer"), new AIChatMessage("user", "Hello")],
            ResponseFormat = AIChatResponseFormat.JsonSchema("response", data.RootElement),
            MaxOutputTokens = 128,
        });

        Assert.Equal("{\"answer\":\"ok\"}", response.Text);
        Assert.Equal("actual-model", response.Model);
        Assert.Equal("apim-1", response.ProviderRequestId);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(8, response.Usage.InputTokens);
        Assert.Equal(4, response.Usage.OutputTokens);
        Assert.Equal(12, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task ResponsesStreamUsesTerminalEventAndMapsIncompleteUsage()
    {
        using var handler = new RecordingHandler(async (message, token) =>
        {
            Assert.Equal("text/event-stream", Assert.Single(message.Headers.Accept).MediaType);
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            return SseResponse(
                Event("""{"type":"response.created","response":{"id":"response-stream","model":"actual-model"}}""") +
                ": heartbeat\r\n\r\nevent: response.output_text.delta\r\ndata: {\"type\":\"response.output_text.delta\",\r\ndata: \"delta\":\"partial\"}\r\n\r\n" +
                Event("""
                    {"type":"response.incomplete","response":{"id":"response-stream","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[],"usage":{"input_tokens":3,"output_tokens":2,"total_tokens":5}}}
                    """));
        });
        using var services = BuildServices(handler);

        var stream = services.GetRequiredService<IAIChatClient>().StreamAsync(CreateRequest());
        var chunks = new List<AIChatStreamChunk>();
        await foreach (var chunk in stream)
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("partial", chunks[0].DeltaText);
        Assert.False(chunks[0].IsFinal);
        Assert.True(chunks[1].IsFinal);
        Assert.Equal("length", chunks[1].FinishReason);
        Assert.Equal(5, chunks[1].Usage!.TotalTokens);
        Assert.Equal("actual-model", chunks[1].Model);
        Assert.Equal("response-stream", chunks[1].ProviderRequestId);
    }

    [Theory]
    [InlineData("data: [DONE]\n\n", AIErrorCodes.ResponseInvalid)]
    [InlineData("data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"server_error\"}}}\n\n", AIErrorCodes.ProviderUnavailable)]
    [InlineData("", AIErrorCodes.ResponseInvalid)]
    public async Task InterruptedOrFailedResponsesStreamIsNotRetriedAfterSendingText(string suffix, string errorCode)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(SseResponse(
            Event("""{"type":"response.output_text.delta","delta":"partial"}""") + suffix)));
        using var services = BuildServices(handler);
        await using var stream = services.GetRequiredService<IAIChatClient>().StreamAsync(CreateRequest()).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("partial", stream.Current.DeltaText);
        var error = await Assert.ThrowsAsync<AIProviderException>(async () => { await stream.MoveNextAsync(); });

        Assert.Equal(errorCode, error.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ExplicitRefusalIsAContentSafetyFailure()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
            {"id":"refusal-1","status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"No."}]}]}
            """)));
        using var services = BuildServices(handler);

        var error = await Assert.ThrowsAsync<AIContentSafetyException>(async () =>
            await services.GetRequiredService<IAIChatClient>().GetResponseAsync(CreateRequest()));

        Assert.Equal("refusal-1", error.ProviderRequestId);
    }

    [Fact]
    public async Task UnsupportedTextFormatUsesTheStandardStructuredOutputError()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"code":"unsupported_parameter","message":"Unsupported parameter: 'text.format'"}""", HttpStatusCode.BadRequest)));
        using var services = BuildServices(handler);

        var error = await Assert.ThrowsAsync<AIProviderException>(async () =>
            await services.GetRequiredService<IAIChatClient>().GetResponseAsync(CreateRequest() with { ResponseFormat = AIChatResponseFormat.JsonObject }));

        Assert.Equal(AIErrorCodes.UnsupportedResponseFormat, error.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    private static AIChatRequest CreateRequest() => new()
    {
        Provider = "Azure",
        Model = ModelName,
        Messages = [new AIChatMessage("user", "Hello")],
    };

    private static ServiceProvider BuildServices(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Chat + ":Resources:main:Endpoint"] = "https://responses.azure.invalid/",
            [Chat + ":Resources:main:Secret"] = "test-key",
            [$"{Chat}:Models:{ModelName}:Resource"] = "main",
            [$"{Chat}:Models:{ModelName}:Deployment"] = "responses-deployment",
            [$"{Chat}:Models:{ModelName}:Api"] = "Responses",
            ["AI:Providers:Azure:MaxRetryAttempts"] = "1",
        }).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage SseResponse(string events) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(events, Encoding.UTF8, "text/event-stream"),
    };

    private static string Event(string data) => $"data: {data}\n\n";

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }
}
