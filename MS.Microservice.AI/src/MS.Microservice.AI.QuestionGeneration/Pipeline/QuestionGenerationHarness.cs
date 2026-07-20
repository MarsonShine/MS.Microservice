using System.Text.Json;
using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Pipeline;

public sealed class QuestionGenerationHarness(
    IQuestionModelClient modelClient,
    QuestionDefinitionRegistry definitions,
    IQuestionPromptProvider prompts,
    IQuestionDuplicateDetector duplicateDetector) : IQuestionGenerationHarness
{
    private static readonly JsonSerializerOptions ComparisonOptions = new(JsonSerializerDefaults.Web);
    private readonly object batchGate = new();
    private readonly List<QuestionReference> acceptedInBatch = [];

    public Task<QuestionGenerationResult> GenerateAsync(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint,
        QuestionGenerationBudget? budget = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            context,
            blueprint,
            budget ?? QuestionGenerationBudget.Balanced,
            observer: null,
            cancellationToken);

    public async Task<QuestionGenerationResult> RunAsync(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint,
        QuestionGenerationBudget budget,
        IGenerationAttemptObserver? observer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = definitions.GetRequired(blueprint.QuestionType);
        var state = new RunState(blueprint, budget, observer);
        var preflight = ValidatePreflight(context, blueprint);
        if (preflight.Count > 0)
        {
            return state.Result(QuestionGenerationStopReason.Ineligible, issues: preflight);
        }

        var eligibility = definition.CheckEligibility(context, blueprint);
        if (!eligibility.IsEligible)
        {
            return state.Result(QuestionGenerationStopReason.Ineligible, issues: eligibility.Issues);
        }

        var draftRequest = new QuestionDraftRequest
        {
            Blueprint = blueprint,
            Context = context,
            Prompt = prompts.Get(blueprint.QuestionType, QuestionPromptStage.Draft),
            ResponseType = definition.CandidateType,
        };
        var draft = await CallWithFormatRetryAsync(
            state,
            QuestionGenerationAttemptStage.Draft,
            repairAttempt: 0,
            inputCandidate: null,
            token => modelClient.DraftAsync(draftRequest, token),
            cancellationToken).ConfigureAwait(false);
        if (state.IsBudgetExceeded)
        {
            return state.Result(QuestionGenerationStopReason.BudgetExceeded);
        }

        if (!draft.IsSuccess)
        {
            return state.Result(MapFailure(draft.Failure, QuestionGenerationStopReason.DraftFailed));
        }

        var candidate = draft.Value!;
        string? previousProgressSignature = null;
        for (var repairAttempt = 0; ; repairAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = ValidateCandidate(candidate, definition, blueprint, context);
            var repairIssues = validation.Issues.Where(issue => issue.IsBlocking).ToArray();
            QuestionEvaluation? evaluation = null;

            if (validation.IsValid)
            {
                var reviewRequest = new QuestionReviewRequest
                {
                    Blueprint = blueprint,
                    Context = context,
                    Prompt = prompts.Get(blueprint.QuestionType, QuestionPromptStage.Review),
                    ResponseType = typeof(QuestionEvaluation),
                    Candidate = candidate,
                    Validation = validation,
                    Rubric = definition.Rubric,
                };
                var review = await CallWithFormatRetryAsync(
                    state,
                    QuestionGenerationAttemptStage.Review,
                    repairAttempt,
                    candidate,
                    token => modelClient.ReviewAsync(reviewRequest, token),
                    cancellationToken).ConfigureAwait(false);
                if (state.IsBudgetExceeded)
                {
                    return state.Result(
                        QuestionGenerationStopReason.BudgetExceeded,
                        candidate,
                        validation);
                }

                if (!review.IsSuccess)
                {
                    return state.Result(
                        MapFailure(review.Failure, QuestionGenerationStopReason.ReviewFailed),
                        candidate,
                        validation);
                }

                evaluation = review.Value!;
                var reviewerIssues = evaluation.Issues ?? [];
                var evaluationIssues = ValidateEvaluation(evaluation, definition.Rubric);
                if (evaluation.Decision == QuestionEvaluationDecision.Accept && evaluationIssues.Count == 0)
                {
                    QuestionDuplicateMatch? duplicate;
                    lock (batchGate)
                    {
                        duplicate = duplicateDetector.FindDuplicate(
                            candidate,
                            definition,
                            context,
                            acceptedInBatch.ToArray());
                    }

                    return duplicate is null
                        ? state.Result(
                            QuestionGenerationStopReason.Accepted,
                            candidate,
                            validation,
                            evaluation)
                        : state.Result(
                            QuestionGenerationStopReason.DuplicateDetected,
                            candidate,
                            validation,
                            evaluation,
                            [
                                new(
                                    "duplicate_question",
                                    QuestionIssueSeverity.Error,
                                    "candidate",
                                    duplicate.Reason,
                                    Repairable: false),
                            ]);
                }

                if (evaluation.Decision == QuestionEvaluationDecision.Reject)
                {
                    return state.Result(
                        QuestionGenerationStopReason.ReviewRejected,
                        candidate,
                        validation,
                        evaluation,
                        reviewerIssues.Concat(evaluationIssues).ToArray());
                }

                repairIssues = reviewerIssues
                    .Concat(evaluationIssues)
                    .Where(issue => issue.IsBlocking)
                    .ToArray();
            }

            if (repairAttempt >= budget.MaxRepairAttempts)
            {
                return state.Result(
                    QuestionGenerationStopReason.RepairExhausted,
                    candidate,
                    validation,
                    evaluation,
                    repairIssues);
            }

            var repairable = repairIssues.Where(issue => issue.Repairable).ToArray();
            if (repairable.Length == 0)
            {
                return state.Result(
                    QuestionGenerationStopReason.ValidationFailed,
                    candidate,
                    validation,
                    evaluation,
                    repairIssues);
            }

            var allowedFields = repairable
                .Select(issue => TopLevelField(issue.Field))
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var signature = CreateProgressSignature(candidate, repairIssues);
            if (string.Equals(signature, previousProgressSignature, StringComparison.Ordinal))
            {
                return state.Result(
                    QuestionGenerationStopReason.NoProgress,
                    candidate,
                    validation,
                    evaluation,
                    repairIssues);
            }

            previousProgressSignature = signature;
            var repairRequest = new QuestionRepairRequest
            {
                Blueprint = blueprint,
                Context = context,
                Prompt = prompts.Get(blueprint.QuestionType, QuestionPromptStage.Repair),
                ResponseType = definition.CandidateType,
                Candidate = candidate,
                Issues = repairIssues,
                Evaluation = evaluation,
                RepairAttempt = repairAttempt + 1,
                AllowedFields = allowedFields,
            };
            var repaired = await CallWithFormatRetryAsync(
                state,
                QuestionGenerationAttemptStage.Repair,
                repairAttempt + 1,
                candidate,
                token => modelClient.RepairAsync(repairRequest, token),
                cancellationToken).ConfigureAwait(false);
            if (state.IsBudgetExceeded)
            {
                return state.Result(
                    QuestionGenerationStopReason.BudgetExceeded,
                    candidate,
                    validation,
                    evaluation);
            }

            if (!repaired.IsSuccess)
            {
                return state.Result(
                    MapFailure(repaired.Failure, QuestionGenerationStopReason.RepairFailed),
                    candidate,
                    validation,
                    evaluation);
            }

            var scopeViolation = FindRepairScopeViolation(
                candidate,
                repaired.Value!,
                definition,
                allowedFields);
            if (scopeViolation is not null)
            {
                return state.Result(
                    QuestionGenerationStopReason.ValidationFailed,
                    repaired.Value,
                    issues: [scopeViolation]);
            }

            candidate = repaired.Value!;
        }
    }

    public bool CommitAccepted(QuestionGenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Accepted || result.Candidate is null)
        {
            return false;
        }

        var definition = definitions.GetRequired(result.Candidate.QuestionType);
        var reference = ExactQuestionDuplicateDetector.CreateReference(result.Candidate, definition);
        lock (batchGate)
        {
            if (acceptedInBatch.Any(item =>
                    string.Equals(item.Fingerprint, reference.Fingerprint, StringComparison.Ordinal)))
            {
                return false;
            }

            acceptedInBatch.Add(reference);
            return true;
        }
    }

    private static IReadOnlyList<QuestionValidationIssue> ValidatePreflight(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint)
    {
        var issues = new List<QuestionValidationIssue>();
        if (!string.Equals(context.Hash, blueprint.ContextHash, StringComparison.Ordinal))
        {
            issues.Add(new(
                "context_hash_mismatch",
                QuestionIssueSeverity.Critical,
                "contextHash",
                "The blueprint and context hashes do not match.",
                Repairable: false));
        }

        if (!string.Equals(context.Version, blueprint.ContextVersion, StringComparison.Ordinal))
        {
            issues.Add(new(
                "context_version_mismatch",
                QuestionIssueSeverity.Critical,
                "contextVersion",
                "The blueprint and context versions do not match.",
                Repairable: false));
        }

        return issues;
    }

    private static QuestionValidationResult ValidateCandidate(
        QuestionCandidate candidate,
        IQuestionDefinition definition,
        QuestionBlueprint blueprint,
        QuestionContextSnapshot context)
    {
        var issues = new List<QuestionValidationIssue>();
        if (candidate.GetType() != definition.CandidateType)
        {
            issues.Add(new(
                "candidate_contract_mismatch",
                QuestionIssueSeverity.Critical,
                "candidate",
                $"Expected '{definition.CandidateType.Name}' but received '{candidate.GetType().Name}'.",
                Repairable: false));
            return new(issues);
        }

        if (!string.Equals(candidate.BlueprintId, blueprint.BlueprintId, StringComparison.Ordinal))
        {
            issues.Add(new(
                "blueprint_id_mismatch",
                QuestionIssueSeverity.Critical,
                "blueprintId",
                "The candidate changed the blueprint identity.",
                Repairable: false));
        }

        if (candidate.QuestionType != blueprint.QuestionType)
        {
            issues.Add(new(
                "question_type_mismatch",
                QuestionIssueSeverity.Critical,
                "questionType",
                "The candidate changed the question type.",
                Repairable: false));
        }

        issues.AddRange(definition.Validate(candidate, blueprint, context).Issues);
        return new(issues);
    }

    private static IReadOnlyList<QuestionValidationIssue> ValidateEvaluation(
        QuestionEvaluation evaluation,
        QuestionReviewRubric rubric)
    {
        var issues = new List<QuestionValidationIssue>();
        if (evaluation.Scores is null)
        {
            return
            [
                new(
                    "review_scores_missing",
                    QuestionIssueSeverity.Error,
                    "scores",
                    "Reviewer did not return a score collection.",
                    Repairable: true),
            ];
        }

        var duplicateDimensions = evaluation.Scores
            .Where(score => !string.IsNullOrWhiteSpace(score.Dimension))
            .GroupBy(score => score.Dimension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateDimensions.Length > 0)
        {
            issues.Add(new(
                "review_dimension_duplicate",
                QuestionIssueSeverity.Error,
                "scores",
                $"Reviewer returned duplicate score dimensions: {string.Join(", ", duplicateDimensions)}.",
                Repairable: true));
        }

        var scores = evaluation.Scores
            .Where(score => !string.IsNullOrWhiteSpace(score.Dimension))
            .GroupBy(score => score.Dimension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Score,
                StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in rubric.RequiredDimensions)
        {
            if (!scores.TryGetValue(dimension, out var score))
            {
                issues.Add(new(
                    "review_dimension_missing",
                    QuestionIssueSeverity.Error,
                    "scores",
                    $"Reviewer did not score required dimension '{dimension}'.",
                    Repairable: true));
                continue;
            }

            if (score is < 0 or > 100)
            {
                issues.Add(new(
                    "review_score_out_of_range",
                    QuestionIssueSeverity.Error,
                    "scores",
                    $"Reviewer score '{dimension}' must be between 0 and 100.",
                    Repairable: true));
            }
            else if (score < rubric.MinimumDimensionScore)
            {
                issues.Add(new(
                    "review_dimension_below_threshold",
                    QuestionIssueSeverity.Error,
                    "candidate",
                    $"Reviewer score '{dimension}' is below the required threshold.",
                    Repairable: true));
            }
        }

        if (evaluation.AverageScore < rubric.MinimumAverageScore)
        {
            issues.Add(new(
                "review_average_below_threshold",
                QuestionIssueSeverity.Error,
                "candidate",
                "Reviewer average score is below the required threshold.",
                Repairable: true));
        }

        return issues;
    }

    private async Task<QuestionModelResult<T>> CallWithFormatRetryAsync<T>(
        RunState state,
        QuestionGenerationAttemptStage stage,
        int repairAttempt,
        QuestionCandidate? inputCandidate,
        Func<CancellationToken, Task<QuestionModelResult<T>>> invoke,
        CancellationToken cancellationToken)
        where T : class
    {
        QuestionModelResult<T>? result = null;
        for (var formatAttempt = 0; formatAttempt <= state.Budget.MaxFormatRetriesPerStage; formatAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.CanCallModel)
            {
                state.IsBudgetExceeded = true;
                return result ?? QuestionModelResult<T>.Failed(new(
                    QuestionModelFailureKind.Permanent,
                    "budget_exceeded",
                    "The model call budget was exhausted.",
                    Retryable: false));
            }

            var sequence = state.NextSequence();
            var invocation = new QuestionGenerationInvocation(
                sequence,
                stage,
                repairAttempt,
                $"{state.Blueprint.BlueprintId}:{stage}:{sequence}");
            if (state.Observer is not null)
            {
                await state.Observer.OnInvocationStartingAsync(invocation, cancellationToken)
                    .ConfigureAwait(false);
            }

            result = await invoke(cancellationToken).ConfigureAwait(false);
            var attempt = new QuestionGenerationAttempt(
                sequence,
                stage,
                repairAttempt,
                result.Metadata,
                result.Failure,
                inputCandidate,
                result.Value as QuestionCandidate,
                Evaluation: result.Value as QuestionEvaluation);
            await state.RecordAsync(attempt, cancellationToken).ConfigureAwait(false);
            if (state.IsBudgetExceeded ||
                result.Failure?.Kind != QuestionModelFailureKind.InvalidStructuredOutput)
            {
                return result;
            }
        }

        return result!;
    }

    private static QuestionValidationIssue? FindRepairScopeViolation(
        QuestionCandidate before,
        QuestionCandidate after,
        IQuestionDefinition definition,
        IReadOnlyList<string> allowedFields)
    {
        if (before.GetType() != after.GetType())
        {
            return new(
                "repair_contract_changed",
                QuestionIssueSeverity.Critical,
                "candidate",
                "Repair changed the candidate contract type.",
                Repairable: false);
        }

        var beforeJson = JsonSerializer.SerializeToElement(before, before.GetType(), ComparisonOptions);
        var afterJson = JsonSerializer.SerializeToElement(after, after.GetType(), ComparisonOptions);
        var beforeProperties = beforeJson.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var afterProperties = afterJson.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var immutable = new HashSet<string>(
            definition.ImmutableFields
                .Concat(["blueprintId", "questionType"]),
            StringComparer.Ordinal);
        var allowed = new HashSet<string>(allowedFields, StringComparer.Ordinal);
        var changedOutsideScope = beforeProperties.Keys
            .Union(afterProperties.Keys, StringComparer.Ordinal)
            .Where(field =>
            {
                var changed = !beforeProperties.TryGetValue(field, out var beforeValue) ||
                    !afterProperties.TryGetValue(field, out var afterValue) ||
                    !JsonElement.DeepEquals(beforeValue, afterValue);
                return changed && (immutable.Contains(field) || !allowed.Contains(field));
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
        return changedOutsideScope.Length == 0
            ? null
            : new(
                "repair_scope_violation",
                QuestionIssueSeverity.Critical,
                "candidate",
                $"Repair modified fields outside its allowlist: {string.Join(", ", changedOutsideScope)}.",
                Repairable: false);
    }

    private static string TopLevelField(string field)
    {
        var separator = field.IndexOfAny(['.', '[']);
        return separator < 0 ? field : field[..separator];
    }

    private static string CreateProgressSignature(
        QuestionCandidate candidate,
        IEnumerable<QuestionValidationIssue> issues)
    {
        var candidateJson = JsonSerializer.Serialize(candidate, candidate.GetType(), ComparisonOptions);
        var issueCodes = string.Join(
            '|',
            issues.Select(issue => $"{issue.Code}:{issue.Field}").Order(StringComparer.Ordinal));
        return $"{candidateJson}|{issueCodes}";
    }

    private static QuestionGenerationStopReason MapFailure(
        QuestionModelFailure? failure,
        QuestionGenerationStopReason fallback) =>
        failure?.Kind switch
        {
            QuestionModelFailureKind.Refusal => QuestionGenerationStopReason.ModelRefusal,
            QuestionModelFailureKind.Truncated => QuestionGenerationStopReason.InvalidStructuredOutput,
            QuestionModelFailureKind.InvalidStructuredOutput => QuestionGenerationStopReason.InvalidStructuredOutput,
            _ => fallback,
        };

    private sealed class RunState(
        QuestionBlueprint blueprint,
        QuestionGenerationBudget budget,
        IGenerationAttemptObserver? observer)
    {
        private readonly List<QuestionGenerationAttempt> attempts = [];
        private int sequence;

        public QuestionBlueprint Blueprint { get; } = blueprint;

        public QuestionGenerationBudget Budget { get; } = budget;

        public IGenerationAttemptObserver? Observer { get; } = observer;

        public bool IsBudgetExceeded { get; set; }

        public int ModelCalls => attempts.Count;

        public int TotalTokens => attempts.Sum(attempt => attempt.Metadata.TotalTokens);

        public decimal TotalEstimatedCost => attempts.Sum(attempt => attempt.Metadata.EstimatedCost);

        public bool CanCallModel =>
            ModelCalls < Budget.MaxModelCalls &&
            TotalTokens < Budget.MaxTotalTokens &&
            (Budget.MaxEstimatedCost is null || TotalEstimatedCost < Budget.MaxEstimatedCost.Value);

        public int NextSequence() => ++sequence;

        public async ValueTask RecordAsync(
            QuestionGenerationAttempt attempt,
            CancellationToken cancellationToken)
        {
            attempts.Add(attempt);
            IsBudgetExceeded =
                ModelCalls > Budget.MaxModelCalls ||
                TotalTokens > Budget.MaxTotalTokens ||
                (Budget.MaxEstimatedCost is not null &&
                 TotalEstimatedCost > Budget.MaxEstimatedCost.Value);
            if (Observer is not null)
            {
                await Observer.OnAttemptRecordedAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        public QuestionGenerationResult Result(
            QuestionGenerationStopReason stopReason,
            QuestionCandidate? candidate = null,
            QuestionValidationResult? validation = null,
            QuestionEvaluation? evaluation = null,
            IReadOnlyList<QuestionValidationIssue>? issues = null) =>
            new()
            {
                Blueprint = Blueprint,
                StopReason = stopReason,
                Candidate = candidate,
                Validation = validation,
                Evaluation = evaluation,
                Issues = issues ?? [],
                Attempts = attempts.ToArray(),
                RepairAttempts = attempts.Count(attempt =>
                    attempt.Stage == QuestionGenerationAttemptStage.Repair),
                ModelCalls = ModelCalls,
                TotalTokens = TotalTokens,
                TotalEstimatedCost = TotalEstimatedCost,
            };
    }
}
