using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.QuestionGeneration.AIChat;
using MS.Microservice.AI.QuestionGeneration.Contracts;
using MS.Microservice.AI.QuestionGeneration.Pipeline;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

public sealed class QuestionGenerationDependencyInjectionTests
{
    [Fact]
    public void AddQuestionGeneration_ShouldRegisterExpectedLifetimes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAIChatClient>(new ThrowingChatClient());
        services.AddSingleton<IAIModelResolver>(new ThrowingResolver());
        services.AddSingleton<ILogger<AIChatQuestionModelClient>>(
            NullLogger<AIChatQuestionModelClient>.Instance);
        services.AddQuestionGeneration().AddDefinition<ShortAnswerDefinition>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IQuestionModelClient>()
            .Should().BeSameAs(provider.GetRequiredService<IQuestionModelClient>());
        provider.GetRequiredService<IQuestionGenerationHarness>()
            .Should().NotBeSameAs(provider.GetRequiredService<IQuestionGenerationHarness>());
        provider.GetRequiredService<QuestionDefinitionRegistry>()
            .GetRequired(ShortAnswerDefinition.TypeId)
            .Should().BeOfType<ShortAnswerDefinition>();
    }

    [Fact]
    public void QuestionGenerationOptions_ShouldNotExposeApiKey()
    {
        typeof(QuestionGenerationOptions).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name => name.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ThrowingChatClient : IAIChatClient
    {
        public ValueTask<AIChatResponse> GetResponseAsync(
            AIChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(
            AIChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingResolver : IAIModelResolver
    {
        public AIResolvedModel ResolveChatModel(AIChatRequest request) => throw new NotSupportedException();
        public AIResolvedModel ResolveTtsModel(AITtsRequest request) => throw new NotSupportedException();
        public AIResolvedModel ResolveAsrModel(AIAsrRequest request) => throw new NotSupportedException();
        public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request) =>
            throw new NotSupportedException();
        public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request) =>
            throw new NotSupportedException();
    }
}
