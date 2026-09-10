using FluentAssertions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class DefaultAIProviderFactoryTests
{
    [Fact]
    public void GetRequiredTtsProvider_ShouldThrowUnsupportedCapability_WhenProviderOnlySupportsChat()
    {
        var factory = new DefaultAIProviderFactory(
            [new FakeChatProvider("DeepSeek")],
            [],
            [],
            [],
            []);

        Action action = () => factory.GetRequiredTtsProvider("DeepSeek");

        var exception = action.Should().Throw<AIUnsupportedCapabilityException>().Which;
        exception.Capability.Should().Be(AICapability.Tts);
        exception.Provider.Should().Be("DeepSeek");
    }

    private sealed class FakeChatProvider : IAIChatProvider
    {
        public FakeChatProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

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