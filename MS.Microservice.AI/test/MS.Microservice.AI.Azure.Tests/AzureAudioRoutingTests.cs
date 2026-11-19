using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;
using Xunit;

namespace MS.Microservice.AI.Azure.Tests;

public sealed class AzureAudioRoutingTests
{
    private const string Provider = "AI:Providers:Azure";
    private static readonly byte[] AudioBytes = "RIFFWAVE"u8.ToArray();

    [Fact]
    public async Task SynthesizeUsesSpeechDeploymentAndReturnsBinaryAudio()
    {
        using var handler = new AudioResponseHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, "speech", "speech");
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var payload = document.RootElement;

            Assert.Equal("audio-speech-deployment", payload.GetProperty("model").GetString());
            Assert.Equal("Azure speech", payload.GetProperty("input").GetString());
            Assert.Equal("alloy", payload.GetProperty("voice").GetString());
            Assert.Equal("wav", payload.GetProperty("response_format").GetString());
            Assert.Equal(1.25, payload.GetProperty("speed").GetDouble());

            var content = new ByteArrayContent(AudioBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var services = BuildServices(handler);
        var client = services.GetRequiredService<IAITtsClient>();

        var response = await client.SynthesizeAsync(new AITtsRequest { Input = "Azure speech", Speed = 1.25 });

        Assert.Equal("Azure", response.Provider);
        Assert.Equal("alloy", response.Voice);
        Assert.Equal("wav", response.ResponseFormat);
        Assert.Equal("audio/wav", response.Audio.ContentType);
        Assert.Equal(AudioBytes, response.Audio.Content);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(null, "json")]
    [InlineData("verbose_json", "verbose_json")]
    public async Task RecognizeUsesTranscriptionDeploymentAndSendsJsonResponseFormat(string? responseFormat, string wireFormat)
    {
        using var handler = new AudioResponseHandler(async (request, cancellationToken) =>
        {
            AssertTarget(request, "transcription", "transcriptions");
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = multipart.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));

            Assert.Equal("audio-transcription-deployment", await parts["model"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("en", await parts["language"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("Product terminology", await parts["prompt"].ReadAsStringAsync(cancellationToken));
            Assert.Equal(wireFormat, await parts["response_format"].ReadAsStringAsync(cancellationToken));
            Assert.Equal("sample.wav", parts["file"].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("audio/wav", parts["file"].Headers.ContentType?.MediaType);
            Assert.Equal(AudioBytes, await parts["file"].ReadAsByteArrayAsync(cancellationToken));

            var body = wireFormat == "verbose_json"
                ? """{"text":"Azure speech","language":"en","segments":[{"text":"Azure speech","start":0.25,"end":1.5}]}"""
                : """{"text":"Azure speech"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });
        using var services = BuildServices(handler);
        var client = services.GetRequiredService<IAIAsrClient>();

        var response = await client.RecognizeAsync(CreateAsrRequest(responseFormat));

        Assert.Equal("Azure", response.Provider);
        Assert.Equal("Azure speech", response.Text);
        Assert.Equal("en", response.Language);
        if (wireFormat == "verbose_json")
        {
            var segment = Assert.Single(response.Segments);
            Assert.Equal("Azure speech", segment.Text);
            Assert.Equal(0.25, segment.StartSeconds);
            Assert.Equal(1.5, segment.EndSeconds);
        }
        else
        {
            Assert.Empty(response.Segments);
        }

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task UnsupportedTranscriptionResponseFormatFailsBeforeNetwork()
    {
        using var handler = new AudioResponseHandler((_, _) => throw new InvalidOperationException("Unexpected request."));
        using var services = BuildServices(handler);
        var client = services.GetRequiredService<IAIAsrClient>();

        await Assert.ThrowsAsync<AIConfigurationException>(async () => await client.RecognizeAsync(CreateAsrRequest("text")));

        Assert.Equal(0, handler.RequestCount);
    }

    private static ServiceProvider BuildServices(AudioResponseHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Provider + ":Resources:speech:Endpoint"] = "https://speech.openai.azure.com",
            [Provider + ":Resources:speech:ApiKey"] = "speech-key",
            [Provider + ":Resources:transcription:Endpoint"] = "https://transcription.openai.azure.com/openai/v1/",
            [Provider + ":Resources:transcription:ApiKey"] = "transcription-key",
            [Provider + ":Deployments:Tts:audio:0:Resource"] = "speech",
            [Provider + ":Deployments:Tts:audio:0:Deployment"] = "audio-speech-deployment",
            [Provider + ":Deployments:Asr:audio:0:Resource"] = "transcription",
            [Provider + ":Deployments:Asr:audio:0:Deployment"] = "audio-transcription-deployment",
            ["AI:Models:Tts:Default:Provider"] = "Azure",
            ["AI:Models:Tts:Default:Model"] = "audio",
            ["AI:Models:Tts:Default:Voice"] = "alloy",
            ["AI:Models:Tts:Default:ResponseFormat"] = "wav",
            ["AI:Models:Asr:Default:Provider"] = "Azure",
            ["AI:Models:Asr:Default:Model"] = "audio",
            ["AI:Models:Asr:Default:Language"] = "en",
        }).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static AIAsrRequest CreateAsrRequest(string? responseFormat) => new()
    {
        Audio = new AIBinaryContent { Content = AudioBytes, ContentType = "audio/wav", FileName = "sample.wav" },
        Prompt = "Product terminology",
        ResponseFormat = responseFormat,
    };

    private static void AssertTarget(HttpRequestMessage request, string resource, string operation)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://{resource}.openai.azure.com/openai/v1/audio/{operation}?api-version=preview", request.RequestUri!.AbsoluteUri);
        Assert.Equal($"{resource}-key", Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Null(request.Headers.Authorization);
    }

    private sealed class AudioResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return respond(request, cancellationToken);
        }
    }
}
