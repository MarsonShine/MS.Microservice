using FluentAssertions;
using MS.Microservice.AI.QuestionGeneration.Contracts;
using MS.Microservice.AI.QuestionGeneration.Pipeline;
using MS.Microservice.AI.QuestionGeneration.Prompts;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

public sealed class QuestionGenerationHarnessTests
{
    [Fact]
    public async Task GenerateAsync_ShouldAcceptValidCandidate()
    {
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate())],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(TestData.AcceptEvaluation())]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.Accepted.Should().BeTrue();
        result.ModelCalls.Should().Be(2);
        harness.CommitAccepted(result).Should().BeTrue();
        harness.CommitAccepted(result).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRepairAndRevalidate()
    {
        var invalid = TestData.Candidate(stem: "");
        var repaired = TestData.Candidate();
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(invalid)],
            repairs: [QuestionModelResult<QuestionCandidate>.Success(repaired)],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(TestData.AcceptEvaluation())]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.Accepted.Should().BeTrue();
        result.RepairAttempts.Should().Be(1);
        result.Attempts.Select(attempt => attempt.Stage).Should().ContainInOrder(
            QuestionGenerationAttemptStage.Draft,
            QuestionGenerationAttemptStage.Repair,
            QuestionGenerationAttemptStage.Review);
    }

    [Fact]
    public async Task GenerateAsync_ShouldRejectRepairOutsideAllowlist()
    {
        var model = new ScriptedModelClient(
            drafts:
            [
                QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate(stem: "")),
            ],
            repairs:
            [
                QuestionModelResult<QuestionCandidate>.Success(
                    TestData.Candidate(answer: "changed immutable answer")),
            ]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.StopReason.Should().Be(QuestionGenerationStopReason.ValidationFailed);
        result.Issues.Should().ContainSingle(issue => issue.Code == "repair_scope_violation");
    }

    [Fact]
    public async Task GenerateAsync_ShouldStopWhenBudgetIsExceeded()
    {
        var metadata = new QuestionModelCallMetadata(
            null,
            "fake",
            "fake",
            "stop",
            60,
            60,
            TimeSpan.Zero);
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate(), metadata)]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(
            TestData.Context(),
            TestData.Blueprint(),
            new QuestionGenerationBudget(maxModelCalls: 2, maxTotalTokens: 100));

        result.StopReason.Should().Be(QuestionGenerationStopReason.BudgetExceeded);
        result.TotalTokens.Should().Be(120);
    }

    [Fact]
    public async Task RunAsync_ShouldReserveInvocationBeforeRecordingAttempt()
    {
        var observer = new RecordingObserver();
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate())],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(TestData.AcceptEvaluation())]);
        var harness = CreateHarness(model);

        await harness.RunAsync(
            TestData.Context(),
            TestData.Blueprint(),
            QuestionGenerationBudget.Balanced,
            observer);

        observer.Events.Should().ContainInOrder(
            "start:1",
            "attempt:1",
            "start:2",
            "attempt:2");
    }

    [Fact]
    public async Task GenerateAsync_ShouldRetryInvalidStructuredOutputOnce()
    {
        var failure = QuestionModelResult<QuestionCandidate>.Failed(new(
            QuestionModelFailureKind.InvalidStructuredOutput,
            "invalid_json",
            "invalid",
            Retryable: true));
        var model = new ScriptedModelClient(
            drafts:
            [
                failure,
                QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate()),
            ],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(TestData.AcceptEvaluation())]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.Accepted.Should().BeTrue();
        result.ModelCalls.Should().Be(3);
    }

    [Fact]
    public async Task GenerateAsync_ShouldDetectExistingDuplicate()
    {
        var reference = ExactQuestionDuplicateDetector.CreateReference(
            TestData.Candidate(),
            new ShortAnswerDefinition());
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate())],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(TestData.AcceptEvaluation())]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(
            TestData.Context([reference]),
            TestData.Blueprint());

        result.StopReason.Should().Be(QuestionGenerationStopReason.DuplicateDetected);
    }

    [Fact]
    public async Task GenerateAsync_ShouldStopWhenReviewerRejects()
    {
        var rejection = new QuestionEvaluation(
            QuestionEvaluationDecision.Reject,
            [new("correctness", 20), new("grounding", 20)],
            [
                new(
                    "incorrect",
                    QuestionIssueSeverity.Error,
                    "answer",
                    "The answer is incorrect.",
                    Repairable: false),
            ],
            "Rejected.");
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(TestData.Candidate())],
            reviews: [QuestionModelResult<QuestionEvaluation>.Success(rejection)]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.StopReason.Should().Be(QuestionGenerationStopReason.ReviewRejected);
    }

    [Fact]
    public async Task GenerateAsync_ShouldStopWhenRepairMakesNoProgress()
    {
        var invalid = TestData.Candidate(stem: "");
        var model = new ScriptedModelClient(
            drafts: [QuestionModelResult<QuestionCandidate>.Success(invalid)],
            repairs: [QuestionModelResult<QuestionCandidate>.Success(invalid)]);
        var harness = CreateHarness(model);

        var result = await harness.GenerateAsync(TestData.Context(), TestData.Blueprint());

        result.StopReason.Should().Be(QuestionGenerationStopReason.NoProgress);
    }

    [Fact]
    public async Task GenerateAsync_ShouldHonorCancellationBeforeCallingModel()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var model = new ScriptedModelClient();
        var harness = CreateHarness(model);

        Func<Task> action = () => harness.GenerateAsync(
            TestData.Context(),
            TestData.Blueprint(),
            cancellationToken: source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static QuestionGenerationHarness CreateHarness(IQuestionModelClient model) =>
        new(
            model,
            new QuestionDefinitionRegistry([new ShortAnswerDefinition()]),
            new DefaultQuestionPromptProvider(),
            new ExactQuestionDuplicateDetector());

    private sealed class RecordingObserver : IGenerationAttemptObserver
    {
        public List<string> Events { get; } = [];

        public ValueTask OnInvocationStartingAsync(
            QuestionGenerationInvocation invocation,
            CancellationToken cancellationToken)
        {
            Events.Add($"start:{invocation.Sequence}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnAttemptRecordedAsync(
            QuestionGenerationAttempt attempt,
            CancellationToken cancellationToken)
        {
            Events.Add($"attempt:{attempt.Sequence}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedModelClient(
        IEnumerable<QuestionModelResult<QuestionCandidate>>? drafts = null,
        IEnumerable<QuestionModelResult<QuestionEvaluation>>? reviews = null,
        IEnumerable<QuestionModelResult<QuestionCandidate>>? repairs = null) : IQuestionModelClient
    {
        private readonly Queue<QuestionModelResult<QuestionCandidate>> draftResults =
            new(drafts ?? []);
        private readonly Queue<QuestionModelResult<QuestionEvaluation>> reviewResults =
            new(reviews ?? []);
        private readonly Queue<QuestionModelResult<QuestionCandidate>> repairResults =
            new(repairs ?? []);

        public Task<QuestionModelResult<QuestionCandidate>> DraftAsync(
            QuestionDraftRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(draftResults.Dequeue());

        public Task<QuestionModelResult<QuestionEvaluation>> ReviewAsync(
            QuestionReviewRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(reviewResults.Dequeue());

        public Task<QuestionModelResult<QuestionCandidate>> RepairAsync(
            QuestionRepairRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(repairResults.Dequeue());
    }
}
