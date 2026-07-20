using System.Text.Json;
using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Tests;

internal sealed record ShortAnswerCandidate : QuestionCandidate
{
    public required string Stem { get; init; }

    public required string Answer { get; init; }
}

internal sealed class ShortAnswerDefinition : IQuestionDefinition
{
    public static QuestionTypeId TypeId { get; } = new("short-answer");

    public QuestionTypeId QuestionType => TypeId;

    public Type CandidateType => typeof(ShortAnswerCandidate);

    public QuestionReviewRubric Rubric { get; } =
        new(["correctness", "grounding"], 80, 70);

    public IReadOnlySet<string> ImmutableFields { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "answer" };

    public QuestionEligibilityResult CheckEligibility(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint) =>
        QuestionEligibilityResult.Eligible;

    public QuestionValidationResult Validate(
        QuestionCandidate candidate,
        QuestionBlueprint blueprint,
        QuestionContextSnapshot context)
    {
        var typed = (ShortAnswerCandidate)candidate;
        return string.IsNullOrWhiteSpace(typed.Stem)
            ? new(
            [
                new(
                    "stem_required",
                    QuestionIssueSeverity.Error,
                    "stem",
                    "Stem is required.",
                    Repairable: true),
            ])
            : QuestionValidationResult.Success;
    }

    public string BuildComparableText(QuestionCandidate candidate)
    {
        var typed = (ShortAnswerCandidate)candidate;
        return $"{typed.Stem} {typed.Answer}";
    }
}

internal static class TestData
{
    public static QuestionContextSnapshot Context(
        IReadOnlyList<QuestionReference>? existing = null) =>
        new()
        {
            ContextId = "context-1",
            Version = "v1",
            Hash = "hash-1",
            Data = JsonSerializer.SerializeToElement(new { topic = "general" }),
            ExistingQuestions = existing ?? [],
        };

    public static QuestionBlueprint Blueprint() =>
        new()
        {
            BlueprintId = "blueprint-1",
            QuestionType = ShortAnswerDefinition.TypeId,
            Sequence = 1,
            ContextVersion = "v1",
            ContextHash = "hash-1",
            SpecificationVersion = "spec-v1",
            Constraints = JsonSerializer.SerializeToElement(new { maxWords = 20 }),
        };

    public static ShortAnswerCandidate Candidate(
        string stem = "What is two plus two?",
        string answer = "four") =>
        new()
        {
            BlueprintId = "blueprint-1",
            QuestionType = ShortAnswerDefinition.TypeId,
            Stem = stem,
            Answer = answer,
        };

    public static QuestionEvaluation AcceptEvaluation() =>
        new(
            QuestionEvaluationDecision.Accept,
            [
                new("correctness", 95),
                new("grounding", 90),
            ],
            [],
            "Accepted.");
}
