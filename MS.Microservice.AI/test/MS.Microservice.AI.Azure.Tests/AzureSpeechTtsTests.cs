using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using Xunit;

namespace MS.Microservice.AI.Azure.Tests;

public sealed class AzureSpeechTtsTests
{
    private static readonly XNamespace Ssml = "http://www.w3.org/2001/10/synthesis";
    private static readonly byte[] Audio = "RIFFWAVE"u8.ToArray();

    [Theory]
    [InlineData("https://eastus.api.cognitive.microsoft.com/", "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")]
    [InlineData("https://eastus.api.cognitive.microsoft.com/sts/v1.0/issueToken", "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")]
    [InlineData(null, "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")]
    [InlineData("https://speech-resource.cognitiveservices.azure.com/", "https://speech-resource.cognitiveservices.azure.com/cognitiveservices/v1")]
    [InlineData("https://eastus.tts.speech.microsoft.com/cognitiveservices/v1", "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")]
    public async Task AzureCloudTtsSendsSpeechSsmlWithTheResourceKey(string? configuredEndpoint, string expectedEndpoint)
    {
        const string input = "Please read <notes> & speak softly.";
        using var handler = new SpeechHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(expectedEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal("speech-resource-key", Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
            Assert.Equal("riff-24khz-16bit-mono-pcm", Assert.Single(request.Headers.GetValues("X-Microsoft-OutputFormat")));
            Assert.False(request.Headers.Contains("api-key"));
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Equal("application/ssml+xml", request.Content!.Headers.ContentType!.MediaType);

            var ssml = XDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(Ssml + "speak", ssml.Root!.Name);
            Assert.Equal("zh-CN", (string?)ssml.Root.Attribute(XNamespace.Xml + "lang"));
            var voice = Assert.Single(ssml.Root.Elements(Ssml + "voice"));
            Assert.Equal("zh-CN-XiaoxiaoNeural", (string?)voice.Attribute("name"));
            var prosody = Assert.Single(voice.Elements(Ssml + "prosody"));
            Assert.Equal("1.25", (string?)prosody.Attribute("rate"));
            Assert.Equal(input, prosody.Value);
            Assert.Empty(prosody.Elements());

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio) };
        });
        using var services = BuildServices(handler, SpeechSettings(configuredEndpoint));

        var result = await services.GetRequiredService<IAITtsClient>().SynthesizeAsync(new AITtsRequest
        {
            Input = input,
            Voice = "zh-CN-XiaoxiaoNeural",
            ResponseFormat = "wav",
            Speed = 1.25,
        });

        Assert.Equal("Azure", result.Provider);
        Assert.Equal("azure-speech", result.Model);
        Assert.Equal("zh-CN-XiaoxiaoNeural", result.Voice);
        Assert.Equal("wav", result.ResponseFormat);
        Assert.Equal("audio/wav", result.Audio.ContentType);
        Assert.Equal("speech.wav", result.Audio.FileName);
        Assert.Equal(Audio, result.Audio.Content);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ExplicitLegacyDefaultIsPreservedWhenAzureCloudTtsAlsoExists()
    {
        using var handler = new SpeechHandler((request, _) =>
        {
            Assert.Equal("https://model.openai.azure.com/openai/v1/audio/speech?api-version=preview", request.RequestUri!.AbsoluteUri);
            Assert.Equal("openai-resource-key", Assert.Single(request.Headers.GetValues("api-key")));
            Assert.False(request.Headers.Contains("Ocp-Apim-Subscription-Key"));
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio) });
        });
        var settings = SpeechSettings(null);
        AddLegacySpeech(settings, "openai-voice");
        settings["AI:Models:Tts:Default:Provider"] = "Azure";
        settings["AI:Models:Tts:Default:Model"] = "openai-voice";
        settings["AI:Models:Tts:Default:Voice"] = "alloy";
        using var services = BuildServices(handler, settings);

        var result = await services.GetRequiredService<IAITtsClient>().SynthesizeAsync(new AITtsRequest { Input = "Hello" });

        Assert.Equal("openai-voice", result.Model);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("AzureCloud:TTS:Secret", " ", "Secret")]
    [InlineData("AzureCloud:TTS:Region", "eastus.example.com/path", "Region")]
    [InlineData("AzureCloud:TTS:Endpoint", "http://eastus.api.cognitive.microsoft.com/", "HTTPS Speech endpoint")]
    [InlineData("AzureCloud:TTS:Endpoint", "https://westus.api.cognitive.microsoft.com/", "region")]
    public void InvalidSpeechResourceFailsOptionsValidation(string setting, string value, string expected)
    {
        using var handler = new SpeechHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP request."));
        var settings = SpeechSettings(null);
        settings[setting] = value;
        using var services = BuildServices(handler, settings);

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains(expected, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void LegacySpeechCannotReuseTheAzureSpeechAliasWhenBothAreConfigured()
    {
        using var handler = new SpeechHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP request."));
        var settings = SpeechSettings(null);
        AddLegacySpeech(settings, "Azure-Speech");
        using var services = BuildServices(handler, settings);

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AIOptions>>().Value);

        Assert.Contains("'azure-speech' is reserved for AzureCloud:TTS", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledAzureProviderRejectsExplicitSpeechBeforeHttp()
    {
        using var handler = new SpeechHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP request."));
        var settings = SpeechSettings(null);
        settings["AI:Providers:Azure:Enabled"] = "false";
        using var services = BuildServices(handler, settings);
        var client = services.GetRequiredService<IAITtsClient>();

        await Assert.ThrowsAsync<AIConfigurationException>(async () => await client.SynthesizeAsync(new AITtsRequest
        {
            Provider = "Azure",
            Model = "azure-speech",
            Input = "Hello",
        }));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SpeechAudioRespectsProviderResponseByteBudget()
    {
        using var handler = new SpeechHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Audio),
        }));
        var settings = SpeechSettings(null);
        settings["AI:Providers:Azure:MaxResponseBytes"] = "4";
        using var services = BuildServices(handler, settings);

        var failure = await Assert.ThrowsAsync<AIProviderException>(async () =>
            await services.GetRequiredService<IAITtsClient>().SynthesizeAsync(new AITtsRequest { Input = "Hello" }));

        Assert.Equal(AIErrorCodes.ResponseInvalid, failure.ErrorCode);
        Assert.Equal(1, handler.RequestCount);
    }

    private static Dictionary<string, string?> SpeechSettings(string? endpoint) => new()
    {
        ["AzureCloud:TTS:Secret"] = "speech-resource-key",
        ["AzureCloud:TTS:Region"] = "eastus",
        ["AzureCloud:TTS:Endpoint"] = endpoint,
        ["AzureCloud:TTS:Voice"] = "en-US-AvaNeural",
    };

    private static void AddLegacySpeech(Dictionary<string, string?> settings, string alias)
    {
        settings["AI:Providers:Azure:Resources:model:Endpoint"] = "https://model.openai.azure.com/";
        settings["AI:Providers:Azure:Resources:model:ApiKey"] = "openai-resource-key";
        settings[$"AI:Providers:Azure:Deployments:Tts:{alias}:0:Resource"] = "model";
        settings[$"AI:Providers:Azure:Deployments:Tts:{alias}:0:Deployment"] = "legacy-tts";
    }

    private static ServiceProvider BuildServices(SpeechHandler handler, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddMicroserviceAI(configuration).AddAzure();
        services.AddHttpClient("MS.Microservice.AI.Azure").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class SpeechHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return respond(request, cancellationToken);
        }
    }
}
