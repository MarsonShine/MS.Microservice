using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.QuestionGeneration.AIChat;
using MS.Microservice.AI.QuestionGeneration.Contracts;
using MS.Microservice.AI.QuestionGeneration.Prompts;
using MS.Microservice.AI.QuestionGeneration.Serialization;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

public sealed class AIChatQuestionModelClientTests
{
    [Fact]
    public async Task DraftAsync_ShouldDowngradeAndCacheUnsupportedSchema()
    {
        var contract = new SystemTextJsonQuestionContract();
        var responseJson = contract.Serialize(TestData.Candidate());
        var chat = new FakeChatClient(
            request => throw UnsupportedFormat(),
            request => Success(responseJson),
            request => Success(responseJson));
        var client = CreateClient(chat, contract);
        var request = CreateDraftRequest();

        var first = await client.DraftAsync(request, CancellationToken.None);
        var second = await client.DraftAsync(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        chat.Requests.Select(item => item.ResponseFormat!.Kind).Should().ContainInOrder(
            AIChatResponseFormatKind.JsonSchema,
            AIChatResponseFormatKind.JsonObject,
            AIChatResponseFormatKind.JsonObject);
    }

    [Fact]
    public async Task DraftAsync_ShouldNotDowngradeAuthenticationFailure()
    {
        var chat = new FakeChatClient(
            request => throw new AIProviderException(
                "bad key",
                AIErrorCodes.ProviderAuthenticationFailed,
                provider: "fake",
                model: "fake",
                statusCode: 401));
        var client = CreateClient(chat, new SystemTextJsonQuestionContract());

        var result = await client.DraftAsync(CreateDraftRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Kind.Should().Be(QuestionModelFailureKind.Permanent);
        chat.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DraftAsync_ShouldReportInvalidStructuredOutput()
    {
        var chat = new FakeChatClient(request => Success("```json\n{}\n```"));
        var client = CreateClient(chat, new SystemTextJsonQuestionContract());

        var result = await client.DraftAsync(CreateDraftRequest(), CancellationToken.None);

        result.Failure!.Kind.Should().Be(QuestionModelFailureKind.InvalidStructuredOutput);
    }

    private static AIChatQuestionModelClient CreateClient(
        IAIChatClient chat,
        IQuestionJsonContract contract) =>
        new(
            chat,
            new FakeResolver(),
            contract,
            Options.Create(new QuestionGenerationOptions()),
            TimeProvider.System,
            NullLogger<AIChatQuestionModelClient>.Instance);

    private static QuestionDraftRequest CreateDraftRequest() =>
        new()
        {
            Blueprint = TestData.Blueprint(),
            Context = TestData.Context(),
            Prompt = new DefaultQuestionPromptProvider().Get(
                ShortAnswerDefinition.TypeId,
                QuestionPromptStage.Draft),
            ResponseType = typeof(ShortAnswerCandidate),
        };

    private static AIProviderException UnsupportedFormat() =>
        new(
            "json_schema is not supported",
            AIErrorCodes.UnsupportedResponseFormat,
            provider: "fake",
            model: "fake",
            statusCode: 400);

    private static AIChatResponse Success(string text) =>
        new()
        {
            Provider = "fake",
            Model = "fake",
            Text = text,
            FinishReason = "stop",
            Usage = new AIUsage { InputTokens = 5, OutputTokens = 7, TotalTokens = 12 },
        };

    private sealed class FakeResolver : IAIModelResolver
    {
        public AIResolvedModel ResolveChatModel(AIChatRequest request) =>
            new()
            {
                Capability = AICapability.Chat,
                Provider = "fake",
                Model = "fake",
                Scenario = request.Scenario ?? "Default",
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetryAttempts = 0,
            };

        public AIResolvedModel ResolveTtsModel(AITtsRequest request) => throw new NotSupportedException();

        public AIResolvedModel ResolveAsrModel(AIAsrRequest request) => throw new NotSupportedException();

        public AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request) =>
            throw new NotSupportedException();

        public AIResolvedModel ResolveImageEditModel(AIImageEditRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class FakeChatClient(params Func<AIChatRequest, AIChatResponse>[] responses) : IAIChatClient
    {
        private readonly Queue<Func<AIChatRequest, AIChatResponse>> queue = new(responses);

        public List<AIChatRequest> Requests { get; } = [];

        public ValueTask<AIChatResponse> GetResponseAsync(
            AIChatRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(queue.Dequeue()(request));
        }

        public IAsyncEnumerable<AIChatStreamChunk> StreamAsync(
            AIChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
