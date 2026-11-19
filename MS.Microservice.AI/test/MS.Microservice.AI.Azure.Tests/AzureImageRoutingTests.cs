using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;
using Xunit;

namespace MS.Microservice.AI.Azure.Tests;

public sealed class AzureImageRoutingTests
{
    private const string Provider = "AI:Providers:Azure";
    private const string EncodedImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1FYAAAAASUVORK5CYII=";

    [Theory]
    [InlineData(null)]
    [InlineData("b64_json")]
    public async Task GenerateUsesImageGenerationDeploymentAndReturnsPng(string? responseFormat)
    {
        using var handler = new ImageResponseHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, "generation", "generations");
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = JsonDocument.Parse(body);

            Assert.Equal("image-generation-deployment", payload.RootElement.GetProperty("model").GetString());
            Assert.Equal("Draw a mountain", payload.RootElement.GetProperty("prompt").GetString());
            Assert.Equal("1024x1024", payload.RootElement.GetProperty("size").GetString());
            Assert.False(payload.RootElement.TryGetProperty("response_format", out _));
        });
        using var services = BuildServices(handler);
        var client = services.GetRequiredService<IAIImageGenerationClient>();

        var response = await client.GenerateAsync(new AIImageGenerationRequest
        {
            Prompt = "Draw a mountain",
            Size = "1024x1024",
            ResponseFormat = responseFormat,
        });

        AssertPng(response);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditUsesImageEditDeploymentAndSendsImageArrayAndOptionalMask(bool withMask)
    {
        using var handler = new ImageResponseHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, "editing", "edits");
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = multipart.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));

            Assert.Equal("image-edit-deployment", await parts["model"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("Remove the background", await parts["prompt"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("source.png", parts["image[]"].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("image/png", parts["image[]"].Headers.ContentType?.MediaType);
            Assert.Equal(Convert.FromBase64String(EncodedImage), await parts["image[]"].ReadAsByteArrayAsync(cancellationToken));
            Assert.DoesNotContain("image", parts.Keys);
            Assert.DoesNotContain("response_format", parts.Keys);
            Assert.Equal(withMask, parts.ContainsKey("mask"));
            if (withMask)
            {
                Assert.Equal("mask.png", parts["mask"].Headers.ContentDisposition!.FileName!.Trim('"'));
            }
        });
        using var services = BuildServices(handler);
        var client = services.GetRequiredService<IAIImageEditClient>();

        var response = await client.EditAsync(new AIImageEditRequest
        {
            Prompt = "Remove the background",
            Image = CreatePng("source.png"),
            Mask = withMask ? CreatePng("mask.png") : null,
            ResponseFormat = "b64_json",
        });

        AssertPng(response);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(AICapability.ImageGeneration)]
    [InlineData(AICapability.ImageEdit)]
    public async Task UrlResponseFormatFailsBeforeNetwork(AICapability capability)
    {
        using var handler = new ImageResponseHandler();
        using var services = BuildServices(handler);

        async Task RequestImage()
        {
            if (capability == AICapability.ImageGeneration)
            {
                await services.GetRequiredService<IAIImageGenerationClient>().GenerateAsync(new AIImageGenerationRequest
                {
                    Prompt = "Draw a mountain",
                    ResponseFormat = "url",
                });
            }
            else
            {
                await services.GetRequiredService<IAIImageEditClient>().EditAsync(new AIImageEditRequest
                {
                    Prompt = "Remove the background",
                    Image = CreatePng("source.png"),
                    ResponseFormat = "url",
                });
            }
        }

        await Assert.ThrowsAsync<AIConfigurationException>(RequestImage);
        Assert.Equal(0, handler.RequestCount);
    }

    private static ServiceProvider BuildServices(ImageResponseHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Provider + ":Resources:generation:Endpoint"] = "https://generation.openai.azure.com",
            [Provider + ":Resources:generation:ApiKey"] = "generation-key",
            [Provider + ":Resources:editing:Endpoint"] = "https://editing.openai.azure.com/openai/v1/",
            [Provider + ":Resources:editing:ApiKey"] = "editing-key",
            [Provider + ":Deployments:ImageGeneration:illustration:0:Resource"] = "generation",
            [Provider + ":Deployments:ImageGeneration:illustration:0:Deployment"] = "image-generation-deployment",
            [Provider + ":Deployments:ImageEdit:illustration:0:Resource"] = "editing",
            [Provider + ":Deployments:ImageEdit:illustration:0:Deployment"] = "image-edit-deployment",
            ["AI:Models:ImageGeneration:Default:Provider"] = "Azure",
            ["AI:Models:ImageGeneration:Default:Model"] = "illustration",
            ["AI:Models:ImageEdit:Default:Provider"] = "Azure",
            ["AI:Models:ImageEdit:Default:Model"] = "illustration",
        }).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static void AssertTarget(HttpRequestMessage request, string resource, string operation)
    {
        Assert.Equal($"https://{resource}.openai.azure.com/openai/v1/images/{operation}?api-version=preview", request.RequestUri!.AbsoluteUri);
        Assert.Equal($"{resource}-key", Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Null(request.Headers.Authorization);
    }

    private static void AssertPng(AIImageResponse response)
    {
        Assert.Equal("Azure", response.Provider);
        var image = Assert.Single(response.Images);
        Assert.Null(image.Url);
        var content = Assert.IsType<AIBinaryContent>(image.Content);
        Assert.Equal("image/png", content.ContentType);
        Assert.Equal(Convert.FromBase64String(EncodedImage), content.Content);
    }

    private static AIBinaryContent CreatePng(string fileName) => new()
    {
        Content = Convert.FromBase64String(EncodedImage),
        ContentType = "image/png",
        FileName = fileName,
    };

    private sealed class ImageResponseHandler(Func<HttpRequestMessage, CancellationToken, Task>? inspect = null) : HttpMessageHandler
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
                Content = new StringContent($$"""{"data":[{"b64_json":"{{EncodedImage}}"}]}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
