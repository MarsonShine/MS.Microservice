using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using Xunit;

namespace MS.Microservice.AI.Azure.Tests;

public sealed class AzureCloudChatRoutingTests
{
    private const string Chat = "AzureCloud:CHAT";

    [Theory]
    [InlineData("gpt-6.2-astra", "main.azure.invalid", "main-key", "astra-deployment", "max_completion_tokens")]
    [InlineData("DEEPSEEK-V4.1-FLASH-1017", "terra.azure.invalid", "terra-key", "deepseek-deployment", "max_tokens")]
    public async Task AzureCloudOnlyRoutesByModelAndUsesTheSelectedResourceKey(
        string model, string host, string key, string deployment, string tokenParameter)
    {
        using var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"https://{host}/openai/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal(key, Assert.Single(request.Headers.GetValues("api-key")));
            Assert.Null(request.Headers.Authorization);

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(deployment, body.RootElement.GetProperty("model").GetString());
            Assert.Equal(256, body.RootElement.GetProperty(tokenParameter).GetInt32());
            Assert.False(body.RootElement.TryGetProperty(tokenParameter == "max_tokens" ? "max_completion_tokens" : "max_tokens", out _));
        });
        var settings = CreateSettings();
        if (tokenParameter == "max_tokens")
        {
            settings[$"{Chat}:Models:{model}:Api"] = "ChatCompletions";
            settings[$"{Chat}:Models:{model}:MaxTokenParameter"] = tokenParameter;
        }

        using var services = BuildServices(settings, handler);
        var client = services.GetRequiredService<IAIChatClient>();

        var response = await client.GetResponseAsync(CreateRequest(model, 256));

        Assert.Equal("Azure", response.Provider);
        Assert.Equal("Hello from Azure", response.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AzureCloudChatDoesNotFallBackToLegacyChatMappings()
    {
        var settings = CreateSettings();
        settings["AI:Providers:Azure:Resources:main:Endpoint"] = "https://legacy.azure.invalid";
        settings["AI:Providers:Azure:Resources:main:ApiKey"] = "legacy-key";
        settings["AI:Providers:Azure:Deployments:Chat:legacy-only:0:Resource"] = "main";
        settings["AI:Providers:Azure:Deployments:Chat:legacy-only:0:Deployment"] = "legacy-deployment";

        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);
        var client = services.GetRequiredService<IAIChatClient>();

        var error = await Assert.ThrowsAsync<AIConfigurationException>(async () =>
            await client.GetResponseAsync(CreateRequest("legacy-only")));

        Assert.Contains("legacy-only", error.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(Chat + ":Models:gpt-6.2-astra:Resource", "missing", "gpt-6.2-astra")]
    [InlineData(Chat + ":Resources:terra:Secret", "", "terra")]
    [InlineData(Chat + ":Models:gpt-6.2-astra:Api", "999", "Api")]
    [InlineData(Chat + ":Models:gpt-6.2-astra:MaxTokenParameter", "max_output_tokens", "MaxTokenParameter")]
    public void InvalidAzureCloudChatIsRejectedDuringOptionsValidation(string path, string value, string errorDetail)
    {
        var settings = CreateSettings();
        settings[path] = value;

        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);

        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains(errorDetail, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
    }

    private static Dictionary<string, string?> CreateSettings() => new()
    {
        [Chat + ":Resources:main:Endpoint"] = "https://main.azure.invalid/",
        [Chat + ":Resources:main:Secret"] = "main-key",
        [Chat + ":Resources:terra:Endpoint"] = "https://terra.azure.invalid/openai/v1/",
        [Chat + ":Resources:terra:Secret"] = "terra-key",
        [Chat + ":Models:gpt-6.2-astra:Resource"] = "main",
        [Chat + ":Models:gpt-6.2-astra:Deployment"] = "astra-deployment",
        [Chat + ":Models:DeepSeek-V4.1-Flash-1017:Resource"] = "TERRA",
        [Chat + ":Models:DeepSeek-V4.1-Flash-1017:Deployment"] = "deepseek-deployment",
    };

    private static AIChatRequest CreateRequest(string model, int? maxOutputTokens = null) => new()
    {
        Provider = "Azure",
        Model = model,
        MaxOutputTokens = maxOutputTokens,
        Messages = [new AIChatMessage("user", "Hello")],
    };

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings, RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task>? inspect = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (inspect is not null)
            {
                await inspect(request, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"Hello from Azure"}}]}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
