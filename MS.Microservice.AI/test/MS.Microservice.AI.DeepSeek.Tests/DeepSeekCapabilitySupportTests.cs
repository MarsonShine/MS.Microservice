using FluentAssertions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

namespace MS.Microservice.AI.DeepSeek.Tests;

public sealed class DeepSeekCapabilitySupportTests
{
    [Fact]
    public void Validate_ShouldFail_WhenDeepSeekIsConfiguredForTts()
    {
        var options = new AIOptions();
        options.Providers.Add(DeepSeekProviderDefaults.ProviderName, new AIProviderRegistrationOptions
        {
            ApiKey = "deepseek-key",
            TimeoutSeconds = 10,
            MaxRetryAttempts = 0,
            ConcurrencyLimit = 1,
        });
        options.Models.Tts.Add("Default", new AITtsModelOptions
        {
            Provider = DeepSeekProviderDefaults.ProviderName,
            Model = "deepseek-audio",
            Voice = "alloy",
        });

        var result = new DeepSeekOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("chat only", StringComparison.OrdinalIgnoreCase));
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