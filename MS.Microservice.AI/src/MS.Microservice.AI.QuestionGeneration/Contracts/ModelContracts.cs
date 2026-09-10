namespace MS.Microservice.AI.QuestionGeneration.Contracts;

public enum QuestionModelFailureKind
{
    Refusal = 1,
    Truncated = 2,
    InvalidStructuredOutput = 3,
    Transient = 4,
    Permanent = 5,
}

public sealed record QuestionModelFailure(
    QuestionModelFailureKind Kind,
    string Code,
    string Message,
    bool Retryable);

public sealed record QuestionModelCallMetadata(
    string? ProviderRequestId,
    string? Provider,
    string? Model,
    string? FinishReason,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    decimal EstimatedCost = 0m)
{
    public static QuestionModelCallMetadata Empty { get; } =
        new(null, null, null, null, 0, 0, TimeSpan.Zero);

    public int TotalTokens => InputTokens + OutputTokens;
}

public sealed record QuestionModelResult<T>(
    T? Value,
    QuestionModelCallMetadata Metadata,
    QuestionModelFailure? Failure)
    where T : class
{
    public bool IsSuccess => Value is not null && Failure is null;

    public static QuestionModelResult<T> Success(T value, QuestionModelCallMetadata? metadata = null) =>
        new(value, metadata ?? QuestionModelCallMetadata.Empty, null);

    public static QuestionModelResult<T> Failed(
        QuestionModelFailure failure,
        QuestionModelCallMetadata? metadata = null) =>
        new(null, metadata ?? QuestionModelCallMetadata.Empty, failure);
}

public abstract record QuestionModelRequest
{
    public required QuestionBlueprint Blueprint { get; init; }

    public required QuestionContextSnapshot Context { get; init; }

    public required QuestionPromptSpecification Prompt { get; init; }

    public required Type ResponseType { get; init; }
}

public sealed record QuestionDraftRequest : QuestionModelRequest;

public sealed record QuestionReviewRequest : QuestionModelRequest
{
    public required QuestionCandidate Candidate { get; init; }

    public required QuestionValidationResult Validation { get; init; }

    public required QuestionReviewRubric Rubric { get; init; }
}

public sealed record QuestionRepairRequest : QuestionModelRequest
{
    public required QuestionCandidate Candidate { get; init; }

    public required IReadOnlyList<QuestionValidationIssue> Issues { get; init; }

    public QuestionEvaluation? Evaluation { get; init; }

    public required int RepairAttempt { get; init; }

    public required IReadOnlyList<string> AllowedFields { get; init; }
}

public interface IQuestionModelClient
{
    Task<QuestionModelResult<QuestionCandidate>> DraftAsync(
        QuestionDraftRequest request,
        CancellationToken cancellationToken);

    Task<QuestionModelResult<QuestionEvaluation>> ReviewAsync(
        QuestionReviewRequest request,
        CancellationToken cancellationToken);

    Task<QuestionModelResult<QuestionCandidate>> RepairAsync(
        QuestionRepairRequest request,
        CancellationToken cancellationToken);
}
