namespace MS.Microservice.AI.QuestionGeneration.Contracts;

public interface IQuestionDefinition
{
    QuestionTypeId QuestionType { get; }

    Type CandidateType { get; }

    QuestionReviewRubric Rubric { get; }

    IReadOnlySet<string> ImmutableFields { get; }

    QuestionEligibilityResult CheckEligibility(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint);

    QuestionValidationResult Validate(
        QuestionCandidate candidate,
        QuestionBlueprint blueprint,
        QuestionContextSnapshot context);

    string BuildComparableText(QuestionCandidate candidate);
}

public sealed class QuestionDefinitionRegistry
{
    private readonly IReadOnlyDictionary<QuestionTypeId, IQuestionDefinition> definitions;

    public QuestionDefinitionRegistry(IEnumerable<IQuestionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var materialized = definitions.ToArray();
        var duplicate = materialized
            .GroupBy(definition => definition.QuestionType)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Question type '{duplicate.Key}' has more than one registered definition.");
        }

        this.definitions = materialized.ToDictionary(definition => definition.QuestionType);
    }

    public IQuestionDefinition GetRequired(QuestionTypeId questionType) =>
        definitions.TryGetValue(questionType, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No question definition is registered for '{questionType}'.");
}

public sealed record QuestionTarget(QuestionTypeId QuestionType, int Count);

public sealed record QuestionPlanningRequest(
    string TaskKey,
    IReadOnlyList<QuestionTarget> Targets,
    QuestionPipelineManifest Manifest);

public sealed record QuestionBlueprintPlan(
    IReadOnlyList<QuestionBlueprint> Blueprints,
    IReadOnlyList<QuestionValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => !issue.IsBlocking);
}

public interface IQuestionBlueprintPlanner
{
    ValueTask<QuestionBlueprintPlan> PlanAsync(
        QuestionPlanningRequest request,
        QuestionContextSnapshot context,
        CancellationToken cancellationToken = default);
}

public enum QuestionPromptStage
{
    Draft = 1,
    Review = 2,
    Repair = 3,
}

public sealed record QuestionPromptSpecification(
    string Id,
    string Version,
    string SystemInstructions,
    string SchemaName);

public interface IQuestionPromptProvider
{
    QuestionPromptSpecification Get(QuestionTypeId questionType, QuestionPromptStage stage);
}

public sealed record QuestionDuplicateMatch(string Fingerprint, string Reason);

public interface IQuestionDuplicateDetector
{
    QuestionDuplicateMatch? FindDuplicate(
        QuestionCandidate candidate,
        IQuestionDefinition definition,
        QuestionContextSnapshot context,
        IReadOnlyCollection<QuestionReference> acceptedInBatch);
}
