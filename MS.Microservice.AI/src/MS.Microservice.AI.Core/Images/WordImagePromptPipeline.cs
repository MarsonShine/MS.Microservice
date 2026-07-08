using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.AI.Core.Images.Pipeline;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Main entry point for word-image prompt generation.
/// Orchestrates parsing, LLM-based plan generation, enrichment, validation, repair,
/// and final prompt assembly into both rich (DB) and safe (Qwen) forms.
/// </summary>
public class WordImagePromptPipeline
{
    private readonly IPlanGeneratorClient planClient;
    private readonly ILogger<WordImagePromptPipeline> logger;

    public WordImagePromptPipeline(IPlanGeneratorClient planClient, ILogger<WordImagePromptPipeline> logger)
    {
        this.planClient = planClient;
        this.logger = logger;
    }

    /// <summary>
    /// Runs the full pipeline and returns both the rich prompt (for DB storage)
    /// and the Qwen-safe prompt (for image generation).
    /// </summary>
    public async Task<(string? RichPrompt, string? SafePrompt)> GeneratePromptsAsync(string wordText, CancellationToken ct = default)
    {
        var input = Parse(wordText);
        WordImagePromptPlan? promptPlan = null;

        try
        {
            promptPlan = await GeneratePlanAsync(input, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build image prompt plan for {WordText}, fallback prompt will be used.", wordText);
        }

        var richPrompt = EducationalFlashcardPromptBuilder.Build(input, promptPlan);
        var safePrompt = QwenSafePromptBuilder.Build(input, promptPlan);

        logger.LogInformation("Image prompt plan for {WordText}: {@PromptPlan}", wordText, promptPlan);
        logger.LogInformation("Qwen-safe prompt for {WordText}: {SafePrompt}", wordText, safePrompt);

        return (richPrompt, safePrompt);
    }

    /// <summary>
    /// Returns only the Qwen-safe prompt (for callers that send directly to DashScope).
    /// </summary>
    public async Task<string?> GenerateSafePromptAsync(string wordText, CancellationToken ct = default)
    {
        var (_, safe) = await GeneratePromptsAsync(wordText, ct);
        return safe;
    }

    /// <summary>
    /// Returns only the rich prompt (for DB storage or debugging).
    /// </summary>
    public async Task<string?> GenerateRichPromptAsync(string wordText, CancellationToken ct = default)
    {
        var (rich, _) = await GeneratePromptsAsync(wordText, ct);
        return rich;
    }

    // ── Plan generation ──

    private async Task<WordImagePromptPlan?> GeneratePlanAsync(WordImageInput input, CancellationToken ct)
    {
        if (input.ContentType == WordImageCardType.Alphabet)
            return await planClient.GenerateAlphabetPlanAsync(input, ct);

        return await GenerateVisualPlanWithPipelineAsync(input, ct);
    }

    private async Task<WordImagePromptPlan?> GenerateVisualPlanWithPipelineAsync(WordImageInput input, CancellationToken ct)
    {
        var visualPlan = await planClient.GenerateVisualPlanAsync(input, ct);
        if (visualPlan == null) return null;

        // Step 1: Deterministic enrichment
        VisualPlanEnricher.Enrich(input, visualPlan);

        // Step 2: Semantic validation
        var issues = VisualPlanValidator.Validate(input, visualPlan);
        if (issues.Count > 0)
        {
            logger.LogWarning("Visual plan has semantic issues for {WordText}: {Issues}", input.RawInput, string.Join("; ", issues));
            VisualPlanRepairer.Repair(input, visualPlan, issues);
        }

        // Step 3: Keep sentence scenes focused; avoid decorative cue accumulation.
        VisualPlanSceneSimplifier.Simplify(input, visualPlan);

        // Step 4: Merge into final plan
        return MergeVisualPlan(input, visualPlan);
    }

    private static WordImagePromptPlan MergeVisualPlan(WordImageInput input, WordImageVisualPlan visualPlan)
    {
        // Build negative elements
        var negativeElements = visualPlan.NegativeElements?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        negativeElements.AddRange(HardcodedNegativeElements);

        if (visualPlan.MustNotShow?.Count > 0)
        {
            foreach (var item in visualPlan.MustNotShow.Where(i => !string.IsNullOrWhiteSpace(i)))
                PromptNormalizer.AddDistinct(negativeElements, item.Trim());
        }

        PromptNormalizer.NormalizeList(negativeElements, 30);

        return new WordImagePromptPlan
        {
            MainSubject = PromptNormalizer.NormalizeValue(visualPlan.VisualMeaning, visualPlan.MainSubject),
            SupportingVisual = visualPlan.SupportingVisual,
            ActionOrGesture = visualPlan.ActionOrGesture,
            SceneSetting = visualPlan.SceneSetting,
            BackgroundHint = visualPlan.BackgroundHint,
            OverlayText = string.Empty,
            AllowVisibleText = false,
            ReserveTextOverlayArea = false,
            NegativeElements = negativeElements,

            SentenceIntent = visualPlan.SentenceIntent,
            PrimaryActor = visualPlan.PrimaryActor,
            SecondaryActor = visualPlan.SecondaryActor,
            RequiredAction = visualPlan.RequiredAction,
            ProhibitedAction = visualPlan.ProhibitedAction,
            WarningCue = visualPlan.WarningCue,
            SafetyCue = visualPlan.SafetyCue,
            SettingCues = visualPlan.SettingCues,
            MustShow = visualPlan.MustShow,
            MustNotShow = visualPlan.MustNotShow
        };
    }

    // ── Input parsing ──

    internal static WordImageInput Parse(string rawInput)
    {
        var normalized = rawInput.Trim();
        var targetText = normalized;
        string? meaningHint = null;

        var bracketIndex = normalized.LastIndexOf('(');
        if (bracketIndex > 0 && normalized.EndsWith(')'))
        {
            targetText = normalized[..bracketIndex].Trim();
            meaningHint = normalized[(bracketIndex + 1)..^1].Trim();
        }

        return new WordImageInput(normalized, targetText, meaningHint, InferCardType(targetText));
    }

    internal static string InferCardType(string targetText)
    {
        if (Regex.IsMatch(targetText, @"^[A-Za-z]$"))
            return WordImageCardType.Alphabet;

        var trimmed = targetText.Trim();
        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasSentencePunctuation = Regex.IsMatch(trimmed, @"[!?.,/:;]");

        if (wordCount >= 4 || hasSentencePunctuation)
            return WordImageCardType.Sentence;

        if (wordCount >= 2)
            return WordImageCardType.Phrase;

        if (Regex.IsMatch(trimmed, @"^[A-Za-z][A-Za-z'’-]*$"))
            return WordImageCardType.Word;

        return WordImageCardType.Abstract;
    }

    // ── Hardcoded negative elements (merged with any plan-level negatives) ──

    private static readonly string[] HardcodedNegativeElements =
    [
        // Eye constraints
        "dot eyes", "beady eyes", "tiny black-dot eyes", "over-simplified facial features", "no iris", "no eye whites",
        // Hair and ethnicity
        "blonde hair on Chinese children", "red hair on Chinese children", "light colored hair on Asian children",
        // Semantic failure
        "generic standing pose", "empty stop gesture without the forbidden action", "standing still instead of running",
        "unrelated action", "unclear scene", "blank studio background for sentence cards",
        // Clothing & footwear
        "barefoot", "bare feet", "no shoes", "no footwear", "naked feet", "bare toes", "inappropriate clothing",
        // Body completeness
        "cropped head", "cropped hands", "cropped feet", "cut-off hands", "cut-off feet",
        "limbs outside the frame", "body parts touching image edges", "extreme close-up", "portrait close-up",
        "half body", "head only", "partial face", "cropped figure", "cut-off at shoulders",
        "missing lower face", "decapitated look", "missing feet", "missing hands",
        "cut-off at ankles", "cut-off at wrists", "floating torso", "no legs",
        // Safety exaggeration
        "injury", "falling", "accident", "blood", "crying in pain", "frightening danger",
        // Sensitive content
        "national flags", "flag symbols", "maps with political borders", "military uniforms",
        "weapons", "political symbols", "religious symbols", "controversial imagery"
    ];
}
