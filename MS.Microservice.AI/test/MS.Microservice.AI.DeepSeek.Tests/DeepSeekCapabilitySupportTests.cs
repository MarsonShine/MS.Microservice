using FluentAssertions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

namespace MS.Microservice.AI.DeepSeek.Tests;

public sealed class DeepSeekCapabilitySupportTests
{
    [Theory]
    [InlineData("DeepSeek", false)]
    [InlineData("deepseek", false)]
    [InlineData("OpenAI", true)]
    public void StaticSelectorsValidateEveryNonChatCapability(string provider, bool valid)
    {
        var options = new AIOptions();
        options.Providers.Add("DeepSeek", new() { ApiKey = "key" });
        options.Models.Tts.Add("speech", new() { Provider = provider });
        options.Models.Asr.Add("transcribe", new() { Provider = provider });
        options.Models.ImageGeneration.Add("generate", new() { Provider = provider });
        options.Models.ImageEdit.Add("edit", new() { Provider = provider });
        var result = new DeepSeekOptionsValidator().Validate(null, options);
        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
            Assert.Equal(4, failures.Count());
            Assert.Contains(failures, failure => failure.Contains("Tts:speech"));
            Assert.Contains(failures, failure => failure.Contains("Asr:transcribe"));
            Assert.Contains(failures, failure => failure.Contains("ImageGeneration:generate"));
            Assert.Contains(failures, failure => failure.Contains("ImageEdit:edit"));
        }
    }

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
