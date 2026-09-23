using FluentAssertions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

namespace MS.Microservice.AI.DeepSeek.Tests;

public sealed class DeepSeekCapabilitySupportTests
{
    [Theory]
    [InlineData("Tts")]
    [InlineData("Asr")]
    [InlineData("ImageGeneration")]
    [InlineData("ImageEdit")]
    public void Validate_ShouldFail_WhenDeepSeekIsConfiguredForUnsupportedCapability(string capability)
    {
        var options = CreateOptions();
        AddModel(options, capability, "Unavailable", "deepseek");

        var result = new DeepSeekOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain($"AI:Models:{capability}:Unavailable");
    }

    [Fact]
    public void Validate_ShouldIgnoreOtherProvidersAndAllowChat()
    {
        var options = CreateOptions();
        AddModel(options, "Tts", "OtherTts", "OpenAI");
        AddModel(options, "Asr", "OtherAsr", "OpenAI");
        AddModel(options, "ImageGeneration", "OtherImage", "OpenAI");
        AddModel(options, "ImageEdit", "OtherEdit", "OpenAI");
        options.Models.Chat.Add("Default", new AIChatModelOptions { Provider = "DeepSeek", Model = "deepseek-chat" });

        new DeepSeekOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldMatchProviderCaseInsensitivelyAcrossMultipleEntries()
    {
        var options = CreateOptions();
        AddModel(options, "Tts", "Other", "OpenAI");
        AddModel(options, "Tts", "Unavailable", "dEePsEeK");

        var result = new DeepSeekOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("AI:Models:Tts:Unavailable");
    }

    [Fact]
    public async Task SynthesizeAsync_ShouldThrowUnsupportedCapability_WhenOnlyChatProviderIsRegistered()
    {
        var client = new RoutingAITtsClient(
            new FakeModelResolver(),
            new DefaultAIProviderFactory(
                [new FakeChatProvider()],
                [],
                [],
                [],
                []));

        Func<Task> action = async () => await client.SynthesizeAsync(new AITtsRequest
        {
            Input = "hello",
        });

        var exception = await action.Should().ThrowAsync<AIUnsupportedCapabilityException>();
        exception.Which.Provider.Should().Be(DeepSeekProviderDefaults.ProviderName);
        exception.Which.Capability.Should().Be(AICapability.Tts);
    }

    private static AIOptions CreateOptions()
    {
        var options = new AIOptions();
        options.Providers.Add(DeepSeekProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "deepseek-key",
            TimeoutSeconds = 10,
            MaxRetryAttempts = 0,
            ConcurrencyLimit = 1,
        });
        return options;
    }

    private static void AddModel(AIOptions options, string capability, string scenario, string provider)
    {
        switch (capability)
        {
            case "Tts":
                options.Models.Tts.Add(scenario, new AITtsModelOptions { Provider = provider, Model = "audio" });
                break;
            case "Asr":
                options.Models.Asr.Add(scenario, new AIAsrModelOptions { Provider = provider, Model = "audio" });
                break;
            case "ImageGeneration":
                options.Models.ImageGeneration.Add(scenario, new AIImageModelOptions { Provider = provider, Model = "image" });
                break;
            case "ImageEdit":
                options.Models.ImageEdit.Add(scenario, new AIImageModelOptions { Provider = provider, Model = "image" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(capability), capability, null);
        }
    }

    private sealed class FakeModelResolver : IAIModelResolver
    {
        public AIResolvedModel ResolveChatModel(AIChatRequest request) => throw new NotSupportedException();

        public AIResolvedModel ResolveTtsModel(AITtsRequest request) => new()
        {
            Capability = AICapability.Tts,
            Provider = DeepSeekProviderDefaults.ProviderName,
            Model = "deepseek-audio",
            Scenario = "Default",
            Timeout = TimeSpan.FromSeconds(10),
            Voice = "alloy",
        };

        public AIResolvedModel ResolveAsrModel(AIAsrRequest request) => throw new NotSupportedException();

        public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request) => throw new NotSupportedException();

        public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request) => throw new NotSupportedException();
    }

    private sealed class FakeChatProvider : IAIChatProvider
    {
        public string Name => DeepSeekProviderDefaults.ProviderName;

        public ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
