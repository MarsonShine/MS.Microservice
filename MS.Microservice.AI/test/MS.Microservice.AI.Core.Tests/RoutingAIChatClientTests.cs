using FluentAssertions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Core.Tests;

public sealed class RoutingAIChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_ShouldDelegateToResolvedProvider()
    {
        var provider = new FakeChatProvider("OpenAI");
        var client = new RoutingAIChatClient(
            new FakeModelResolver(new AIResolvedModel
            {
                Provider = "OpenAI",
                Model = "gpt-4.1-mini",
                Timeout = TimeSpan.FromSeconds(100),
            }),
            new FakeProviderFactory(provider));

        var response = await client.GetResponseAsync(new AIChatRequest
        {
            Messages = [new AIChatMessage("user", "hello")],
        });

        response.Provider.Should().Be("OpenAI");
        response.Text.Should().Be("provider-response");
        provider.GetResponseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StreamAsync_ShouldDelegateToResolvedProvider()
    {
        var provider = new FakeChatProvider("DeepSeek");
        var client = new RoutingAIChatClient(
            new FakeModelResolver(new AIResolvedModel
            {
                Provider = "DeepSeek",
                Model = "deepseek-v4-pro",
                Timeout = TimeSpan.FromSeconds(80),
            }),
            new FakeProviderFactory(provider));

        var chunks = new List<AIChatStreamChunk>();
        await foreach (var chunk in client.StreamAsync(new AIChatRequest
                       {
                           Messages = [new AIChatMessage("user", "hello")],
                       }))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(2);
        chunks.Last().IsFinal.Should().BeTrue();
        provider.StreamCallCount.Should().Be(1);
    }

    [Fact]
    public void GetResponseAsync_ShouldThrow_WhenMessagesAreMissing()
    {
        var client = new RoutingAIChatClient(
            new FakeModelResolver(new AIResolvedModel
            {
                Provider = "OpenAI",
                Model = "gpt-4.1-mini",
                Timeout = TimeSpan.FromSeconds(10),
            }),
            new FakeProviderFactory(new FakeChatProvider("OpenAI")));

        Func<Task> action = async () => await client.GetResponseAsync(new AIChatRequest
        {
            Messages = [],
        });

        action.Should().ThrowAsync<AIConfigurationException>()
            .WithMessage("*at least one message*");
    }

    private sealed class FakeModelResolver : IAIModelResolver
    {
        private readonly AIResolvedModel _resolvedModel;

        public FakeModelResolver(AIResolvedModel resolvedModel)
        {
            _resolvedModel = resolvedModel;
        }

        public AIResolvedModel ResolveChatModel(AIChatRequest request) => _resolvedModel;
    }

    private sealed class FakeProviderFactory : IAIProviderFactory
    {
        private readonly IAIChatProvider _provider;

        public FakeProviderFactory(IAIChatProvider provider)
        {
            _provider = provider;
        }

        public IAIChatProvider GetRequiredChatProvider(string providerName) => _provider;
    }

    private sealed class FakeChatProvider : IAIChatProvider
    {
        public FakeChatProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public int GetResponseCallCount { get; private set; }

        public int StreamCallCount { get; private set; }

        public ValueTask<AIChatResponse> GetResponseAsync(AIResolvedModel model, AIChatRequest request, CancellationToken cancellationToken = default)
        {
            GetResponseCallCount++;
            return ValueTask.FromResult(new AIChatResponse
            {
                Provider = Name,
                Model = model.Model,
                Text = "provider-response",
            });
        }

        public async IAsyncEnumerable<AIChatStreamChunk> StreamAsync(AIResolvedModel model, AIChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCallCount++;
            yield return new AIChatStreamChunk
            {
                Provider = Name,
                Model = model.Model,
                DeltaText = "hello",
            };

            await Task.Yield();

            yield return new AIChatStreamChunk
            {
                Provider = Name,
                Model = model.Model,
                IsFinal = true,
                FinishReason = "stop",
            };
        }
    }
}