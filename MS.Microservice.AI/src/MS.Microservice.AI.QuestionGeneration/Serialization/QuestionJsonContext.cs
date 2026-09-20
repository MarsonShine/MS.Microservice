using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Serialization;

internal sealed record QuestionDraftEnvelope(QuestionBlueprint Blueprint, QuestionContextSnapshot Context);
internal sealed record QuestionReviewEnvelope(QuestionBlueprint Blueprint, QuestionContextSnapshot Context,
    JsonElement Candidate, QuestionValidationResult Validation, QuestionReviewRubric Rubric);
internal sealed record QuestionRepairEnvelope(QuestionBlueprint Blueprint, QuestionContextSnapshot Context,
    JsonElement Candidate, IReadOnlyList<QuestionValidationIssue> Issues, QuestionEvaluation? Review,
    int RepairAttempt, IReadOnlyList<string> AllowedFields);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.Strict, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(QuestionDraftEnvelope))]
[JsonSerializable(typeof(QuestionReviewEnvelope))]
[JsonSerializable(typeof(QuestionRepairEnvelope))]
[JsonSerializable(typeof(QuestionEvaluation))]
internal partial class QuestionJsonContext : JsonSerializerContext;
