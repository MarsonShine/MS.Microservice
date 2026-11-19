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

public sealed class AzureChatRoutingTests
{
    private const string ProviderPath = "Providers:Azure";
    private const string Primary = ProviderPath + ":Resources:primary";
    private const string Secondary = ProviderPath + ":Resources:secondary";
    private const string Chat = ProviderPath + ":Deployments:Chat";

    [Theory]
    [InlineData(null, "primary.openai.azure.com", "primary-key", "assistant-primary")]
    [InlineData(Chat + ":assistant:0:Enabled", "secondary.openai.azure.com", "secondary-key", "assistant-secondary")]
    [InlineData(Primary + ":Enabled", "secondary.openai.azure.com", "secondary-key", "assistant-secondary")]
    public async Task ScenarioUsesFirstEnabledDeploymentAndItsResourceCredentials(
        string? disabledSetting, string host, string apiKey, string deployment)
    {
        var settings = CreateSettings();
        if (disabledSetting is not null)
        {
            settings[disabledSetting] = "false";
        }

        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);
        var client = services.GetRequiredService<IAIChatClient>();

        var response = await client.GetResponseAsync(CreateRequest());

        Assert.Equal("Azure", response.Provider);
        Assert.Equal("hello", response.Text);
        var sent = Assert.Single(handler.Requests);
        AssertTarget(sent, host, apiKey, deployment);
        Assert.Equal(64, sent.Body.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(sent.Body.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task ExplicitModelSelectsItsDeploymentForStreaming()
    {
        const string events = """
            data: {"choices":[],"prompt_filter_results":[]}

            data: {"choices":[{"delta":{"content":"hello"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: {"choices":[],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}

            data: [DONE]

            """;

        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(events, Encoding.UTF8, "text/event-stream"),
        });
        using var services = BuildServices(CreateSettings(), handler);
        var client = services.GetRequiredService<IAIChatClient>();
        var request = CreateRequest() with { Provider = "azure", Model = "reasoning", Scenario = "Default" };

        var chunks = new List<AIChatStreamChunk>();
        await foreach (var chunk in client.StreamAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.Collection(chunks,
            chunk => Assert.Equal("hello", chunk.DeltaText),
            chunk =>
            {
                Assert.True(chunk.IsFinal);
                Assert.Equal("stop", chunk.FinishReason);
                Assert.Equal(3, chunk.Usage?.TotalTokens);
            });
        var sent = Assert.Single(handler.Requests);
        AssertTarget(sent, "secondary.openai.azure.com", "secondary-key", "reasoning-secondary");
        Assert.True(sent.Body.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task ExplicitConfigurationSectionSupportsAResourceSecretName()
    {
        const string secretName = "AzureTestSecrets:Primary";
        var settings = CreateSettings();
        settings.Remove(Primary + ":ApiKey");
        settings[Primary + ":ApiKeySecretName"] = secretName;

        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler, "CustomAI", secretName, "resolved-key");
        var client = services.GetRequiredService<IAIChatClient>();

        await client.GetResponseAsync(CreateRequest());

        AssertTarget(Assert.Single(handler.Requests), "primary.openai.azure.com", "resolved-key", "assistant-primary");
    }

    [Theory]
    [InlineData(Primary + ":Endpoint", "http://primary.openai.azure.com/", "Endpoint")]
    [InlineData(Primary + ":Endpoint", "https://primary.openai.azure.com/other/path", "Endpoint")]
    [InlineData(Primary + ":ApiKey", null, "ApiKey")]
    [InlineData("Models:Chat:Default:Model", "missing-alias", "missing-alias")]
    public void InvalidConfigurationFailsBeforeNetwork(string setting, string? value, string errorDetail)
    {
        var settings = CreateSettings();
        settings[setting] = value;

        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);

        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains(errorDetail, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnmappedExplicitModelFailsBeforeNetwork()
    {
        using var handler = new RecordingHandler();
        using var services = BuildServices(CreateSettings(), handler);
        var client = services.GetRequiredService<IAIChatClient>();
        var request = CreateRequest() with { Provider = "Azure", Model = "missing-alias" };

        var error = await Assert.ThrowsAsync<AIConfigurationException>(async () => await client.GetResponseAsync(request));

        Assert.Contains("missing-alias", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    private static Dictionary<string, string?> CreateSettings() => new()
    {
        ["Models:Chat:Default:Provider"] = "Azure",
        ["Models:Chat:Default:Model"] = "assistant",
        ["Models:Chat:Default:MaxOutputTokens"] = "64",
        [Primary + ":Endpoint"] = "https://primary.openai.azure.com",
        [Primary + ":ApiKey"] = "primary-key",
        [Secondary + ":Endpoint"] = "https://secondary.openai.azure.com/openai/v1/",
        [Secondary + ":ApiKey"] = "secondary-key",
        [Chat + ":assistant:0:Resource"] = "primary",
        [Chat + ":assistant:0:Deployment"] = "assistant-primary",
        [Chat + ":assistant:1:Resource"] = "secondary",
        [Chat + ":assistant:1:Deployment"] = "assistant-secondary",
        [Chat + ":reasoning:0:Resource"] = "secondary",
        [Chat + ":reasoning:0:Deployment"] = "reasoning-secondary",
    };

    private static ServiceProvider BuildServices(
        Dictionary<string, string?> settings, RecordingHandler handler,
        string section = AIOptions.SectionName, string? secretName = null, string? secretValue = null)
    {
        var values = settings.ToDictionary(pair => $"{section}:{pair.Key}", pair => pair.Value);
        if (secretName is not null)
        {
            values.Add(secretName, secretValue);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        var builder = section == AIOptions.SectionName
            ? services.AddMicroserviceAI(configuration)
            : services.AddMicroserviceAI(configuration.GetSection(section));
        builder.AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static AIChatRequest CreateRequest() => new()
    {
        Messages = [new AIChatMessage("user", "Hello")],
    };

    private static void AssertTarget(SentRequest sent, string host, string apiKey, string deployment)
    {
        Assert.Equal($"https://{host}/openai/v1/chat/completions", sent.Uri.AbsoluteUri);
        Assert.Equal(apiKey, Assert.Single(sent.ApiKeys));
        Assert.False(sent.HasAuthorization);
        Assert.Equal(deployment, sent.Body.GetProperty("model").GetString());
    }

    private sealed record SentRequest(Uri Uri, string[] ApiKeys, bool HasAuthorization, JsonElement Body);

    private sealed class RecordingHandler(Func<HttpResponseMessage>? response = null) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = JsonDocument.Parse(json);
            Requests.Add(new SentRequest(request.RequestUri!,
                request.Headers.TryGetValues("api-key", out var keys) ? keys.ToArray() : [],
                request.Headers.Contains("Authorization"),
                payload.RootElement.Clone()));
            return response?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"hello"}}]}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
