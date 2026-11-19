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

public sealed class AzureCloudImageRoutingTests
{
    private const string Image = "AzureCloud:IMAGE";
    private const string Native = Image + ":Models:native";
    private const string V1 = Image + ":Models:v1";
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1FYAAAAASUVORK5CYII=";

    [Theory]
    [InlineData("native", null, "native.azure.invalid", "native-key", "/openai/deployments/native-deployment/images/generations?api-version=2025-04-01-preview", null)]
    [InlineData("native", "2025-04-01", "native.azure.invalid", "native-key", "/openai/deployments/native-deployment/images/generations?api-version=2025-04-01", null)]
    [InlineData("v1", null, "v1.azure.invalid", "v1-key", "/openai/v1/images/generations?api-version=preview", "v1-deployment")]
    public async Task GenerationUsesConfiguredImageApiAndResource(
        string alias, string? version, string host, string key, string path, string? bodyModel)
    {
        var settings = CreateSettings();
        if (version is not null)
        {
            settings[Native + ":ApiVersion"] = version;
        }

        // Identical aliases in CHAT resolve solely through the separate Chat resource namespace.
        settings["AzureCloud:CHAT:Resources:main:Endpoint"] = "https://chat.azure.invalid";
        settings["AzureCloud:CHAT:Resources:main:Secret"] = "chat-key";
        settings["AzureCloud:CHAT:Models:native:Resource"] = "main";
        settings["AzureCloud:CHAT:Models:native:Deployment"] = "chat-deployment";

        using var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, host, key, path);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement;
            Assert.Equal("Draw a mountain", body.GetProperty("prompt").GetString());
            Assert.Equal("1024x1024", body.GetProperty("size").GetString());
            Assert.Equal("medium", body.GetProperty("quality").GetString());
            Assert.Equal(1, body.GetProperty("n").GetInt32());
            Assert.False(body.TryGetProperty("response_format", out _));
            Assert.Equal(bodyModel is not null, body.TryGetProperty("model", out var model));
            if (bodyModel is not null)
            {
                Assert.Equal(bodyModel, model.GetString());
            }
        });
        using var services = BuildServices(settings, handler);

        var response = await services.GetRequiredService<IAIImageGenerationClient>().GenerateAsync(new AIImageGenerationRequest
        {
            Provider = "Azure",
            Model = alias,
            Prompt = "Draw a mountain",
            Size = "1024x1024",
            Quality = "medium",
        });

        AssertImage(response, alias);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("native", "native.azure.invalid", "native-key", "/openai/deployments/native-deployment/images/edits?api-version=2025-04-01-preview", "image", null)]
    [InlineData("v1", "v1.azure.invalid", "v1-key", "/openai/v1/images/edits?api-version=preview", "image[]", "v1-deployment")]
    public async Task EditingUsesApiSpecificImagePartAndDeploymentName(
        string alias, string host, string key, string path, string imagePartName, string? bodyModel)
    {
        using var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, host, key, path);
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = multipart.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("Edit the source image", await parts["prompt"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("source.png", parts[imagePartName].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal(Convert.FromBase64String(Png), await parts[imagePartName].ReadAsByteArrayAsync(cancellationToken));
            Assert.Equal("mask.png", parts["mask"].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.DoesNotContain("response_format", parts.Keys);
            Assert.Equal(bodyModel is not null, parts.ContainsKey("model"));
            if (bodyModel is not null)
            {
                Assert.Equal(bodyModel, await parts["model"].ReadAsStringAsync(cancellationToken));
            }
        });
        using var services = BuildServices(CreateSettings(), handler);

        var response = await services.GetRequiredService<IAIImageEditClient>().EditAsync(new AIImageEditRequest
        {
            Provider = "Azure",
            Model = alias,
            Prompt = "Edit the source image",
            Image = CreatePng("source.png"),
            Mask = CreatePng("mask.png"),
            ResponseFormat = "b64_json",
        });

        AssertImage(response, alias);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(Native + ":Api", "Responses", "Api")]
    [InlineData(Native + ":ApiVersion", "2025-02-31-preview", "ApiVersion")]
    [InlineData(V1 + ":ApiVersion", "2025-04-01", "ApiVersion")]
    [InlineData(Native + ":Deployment", "..", "Deployment")]
    public void InvalidImageRouteFailsBeforeHttp(string setting, string value, string failure)
    {
        var settings = CreateSettings();
        settings[setting] = value;
        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);

        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains(failure, error.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AzureCloudImageTakesPrecedenceOverLegacyImageRoutes()
    {
        var settings = CreateSettings();
        const string legacy = "AI:Providers:Azure";
        settings[legacy + ":Resources:main:Endpoint"] = "https://legacy.azure.invalid";
        settings[legacy + ":Resources:main:ApiKey"] = "legacy-key";
        settings[legacy + ":Deployments:ImageEdit:legacy:0:Resource"] = "main";
        settings[legacy + ":Deployments:ImageEdit:legacy:0:Deployment"] = "legacy-deployment";
        using var handler = new RecordingHandler();
        using var services = BuildServices(settings, handler);

        var error = await Assert.ThrowsAsync<AIConfigurationException>(async () =>
            await services.GetRequiredService<IAIImageEditClient>().EditAsync(new AIImageEditRequest
            {
                Provider = "Azure",
                Model = "legacy",
                Prompt = "Edit the source image",
                Image = CreatePng("source.png"),
            }));

        Assert.Contains("legacy", error.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    private static Dictionary<string, string?> CreateSettings() => new()
    {
        [Image + ":Resources:main:Endpoint"] = "https://native.azure.invalid/openai/v1/",
        [Image + ":Resources:main:Secret"] = "native-key",
        [Image + ":Resources:alternate:Endpoint"] = "https://v1.azure.invalid",
        [Image + ":Resources:alternate:Secret"] = "v1-key",
        [Native + ":Resource"] = "main",
        [Native + ":Deployment"] = "native-deployment",
        [V1 + ":Resource"] = "alternate",
        [V1 + ":Deployment"] = "v1-deployment",
        [V1 + ":Api"] = "OpenAIV1",
    };

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings, RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static void AssertTarget(HttpRequestMessage request, string host, string key, string path)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://{host}{path}", request.RequestUri!.AbsoluteUri);
        Assert.Equal(key, Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Null(request.Headers.Authorization);
    }

    private static void AssertImage(AIImageResponse response, string alias)
    {
        Assert.Equal("Azure", response.Provider);
        Assert.Equal(alias, response.Model);
        var content = Assert.IsType<AIBinaryContent>(Assert.Single(response.Images).Content);
        Assert.Equal("image/png", content.ContentType);
        Assert.Equal(Convert.FromBase64String(Png), content.Content);
    }

    private static AIBinaryContent CreatePng(string name) => new()
    {
        Content = Convert.FromBase64String(Png),
        ContentType = "image/png",
        FileName = name,
    };

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
                Content = new StringContent($$"""{"data":[{"b64_json":"{{Png}}"}]}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
