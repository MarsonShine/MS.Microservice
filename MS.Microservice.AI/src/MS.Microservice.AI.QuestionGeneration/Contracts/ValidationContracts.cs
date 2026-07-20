namespace MS.Microservice.AI.QuestionGeneration.Contracts;

public enum QuestionIssueSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}

public sealed record QuestionValidationIssue(
    string Code,
    QuestionIssueSeverity Severity,
    string Field,
    string Message,
    bool Repairable)
{
    public bool IsBlocking => Severity is QuestionIssueSeverity.Error or QuestionIssueSeverity.Critical;
}

public sealed record QuestionValidationResult(IReadOnlyList<QuestionValidationIssue> Issues)
{
    public static QuestionValidationResult Success { get; } = new([]);

    public bool IsValid => Issues.All(issue => !issue.IsBlocking);
}

public sealed record QuestionEligibilityResult(IReadOnlyList<QuestionValidationIssue> Issues)
{
    public static QuestionEligibilityResult Eligible { get; } = new([]);

    public bool IsEligible => Issues.All(issue => !issue.IsBlocking);
}

public enum QuestionEvaluationDecision
{
    Accept = 1,
    Repair = 2,
    Reject = 3,
}

public sealed record QuestionEvaluationScore(string Dimension, int Score);

public sealed record QuestionReviewRubric(
    IReadOnlyList<string> RequiredDimensions,
    int MinimumAverageScore = 80,
    int MinimumDimensionScore = 70);

public sealed record QuestionEvaluation(
    QuestionEvaluationDecision Decision,
    IReadOnlyList<QuestionEvaluationScore> Scores,
    IReadOnlyList<QuestionValidationIssue> Issues,
    string Summary)
{
    public int AverageScore => Scores.Count == 0 ? 0 : (int)Scores.Average(score => score.Score);
}
