using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Pipeline;

public enum QuestionGenerationStopReason
{
    Accepted = 1,
    Ineligible = 2,
    DraftFailed = 3,
    ReviewFailed = 4,
    RepairFailed = 5,
    ModelRefusal = 6,
    InvalidStructuredOutput = 7,
    ValidationFailed = 8,
    ReviewRejected = 9,
    RepairExhausted = 10,
    NoProgress = 11,
    BudgetExceeded = 12,
    DuplicateDetected = 13,
}

public enum QuestionGenerationAttemptStage
{
    Draft = 1,
    Review = 2,
    Repair = 3,
}

public sealed record QuestionGenerationInvocation(
    int Sequence,
    QuestionGenerationAttemptStage Stage,
    int RepairAttempt,
    string InvocationId);

public sealed record QuestionGenerationAttempt(
    int Sequence,
    QuestionGenerationAttemptStage Stage,
    int RepairAttempt,
    QuestionModelCallMetadata Metadata,
    QuestionModelFailure? Failure,
    QuestionCandidate? InputCandidate = null,
    QuestionCandidate? OutputCandidate = null,
    QuestionValidationResult? Validation = null,
    QuestionEvaluation? Evaluation = null,
    IReadOnlyList<QuestionValidationIssue>? Issues = null);

public interface IGenerationAttemptObserver
{
    ValueTask OnInvocationStartingAsync(
        QuestionGenerationInvocation invocation,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    ValueTask OnAttemptRecordedAsync(
        QuestionGenerationAttempt attempt,
        CancellationToken cancellationToken);
}

public sealed record QuestionGenerationBudget
{
    public QuestionGenerationBudget(
        int maxRepairAttempts = 2,
        int maxModelCalls = 7,
        int maxTotalTokens = 30_000,
        decimal? maxEstimatedCost = null,
        int maxFormatRetriesPerStage = 1)
    {
        if (maxRepairAttempts is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRepairAttempts));
        }

        if (maxModelCalls <= 0 || maxTotalTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxModelCalls), "Call and token budgets must be positive.");
        }

        if (maxEstimatedCost is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEstimatedCost));
        }

        if (maxFormatRetriesPerStage is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFormatRetriesPerStage));
        }

        MaxRepairAttempts = maxRepairAttempts;
        MaxModelCalls = maxModelCalls;
        MaxTotalTokens = maxTotalTokens;
        MaxEstimatedCost = maxEstimatedCost;
        MaxFormatRetriesPerStage = maxFormatRetriesPerStage;
    }

    public int MaxRepairAttempts { get; }

    public int MaxModelCalls { get; }

    public int MaxTotalTokens { get; }

    public decimal? MaxEstimatedCost { get; }

    public int MaxFormatRetriesPerStage { get; }

    public static QuestionGenerationBudget Balanced { get; } = new();
}

public sealed record QuestionGenerationResult
{
    public required QuestionBlueprint Blueprint { get; init; }

    public required QuestionGenerationStopReason StopReason { get; init; }

    public bool Accepted => StopReason == QuestionGenerationStopReason.Accepted;

    public QuestionCandidate? Candidate { get; init; }

    public QuestionValidationResult? Validation { get; init; }

    public QuestionEvaluation? Evaluation { get; init; }

    public IReadOnlyList<QuestionValidationIssue> Issues { get; init; } = [];

    public IReadOnlyList<QuestionGenerationAttempt> Attempts { get; init; } = [];

    public int RepairAttempts { get; init; }

    public int ModelCalls { get; init; }

    public int TotalTokens { get; init; }

    public decimal TotalEstimatedCost { get; init; }
}

public interface IQuestionGenerationHarness
{
    Task<QuestionGenerationResult> GenerateAsync(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint,
        QuestionGenerationBudget? budget = null,
        CancellationToken cancellationToken = default);

    Task<QuestionGenerationResult> RunAsync(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint,
        QuestionGenerationBudget budget,
        IGenerationAttemptObserver? observer,
        CancellationToken cancellationToken = default);

    bool CommitAccepted(QuestionGenerationResult result);
}
