using System.Text.Json;
using System.Text.RegularExpressions;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images.Models;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Default implementation of <see cref="IPlanGeneratorClient"/> that uses
/// <see cref="IAIChatClient"/> to call an LLM for visual plan generation.
/// The prompts are designed for educational flashcard illustrations.
/// </summary>
/// <remarks>
/// <para>
/// Model resolution follows the framework's scenario-based pattern:
/// set <c>AI:Models:Chat:ImagePromptPlanning</c> in configuration
/// to control which provider/model is used for prompt planning.
/// </para>
/// <para>
/// The scenario can be customized via the constructor. When
/// <see cref="ResolveModel"/> returns a non-null value, it is used as a
/// direct model override; otherwise, the configured scenario is used.
/// </para>
/// </remarks>
public partial class PlanGeneratorClient : IPlanGeneratorClient
{
    /// <summary>The default scenario key for image prompt planning model resolution.</summary>
    public const string DefaultScenario = "ImagePromptPlanning";

    private readonly IAIChatClient chatClient;
    private readonly ILogger<PlanGeneratorClient> logger;
    private readonly string scenario;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Initializes a new instance of <see cref="PlanGeneratorClient"/>.
    /// </summary>
    /// <param name="chatClient">The chat client for LLM calls.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="scenario">
    /// The chat scenario key used to resolve the model via
    /// <c>AI:Models:Chat:{scenario}</c> in application configuration.
    /// Defaults to <see cref="DefaultScenario"/>.
    /// </param>
    public PlanGeneratorClient(IAIChatClient chatClient, ILogger<PlanGeneratorClient> logger, string scenario = DefaultScenario)
    {
        this.chatClient = chatClient;
        this.logger = logger;
        this.scenario = scenario;
    }

    /// <inheritdoc />
    public async Task<T?> SendAsJsonAsync<T>(string systemPrompt, string userMessage, string? model, CancellationToken ct = default)
        where T : class
    {
        // When a specific model override is provided, use it directly.
        // Otherwise, rely on scenario-based resolution via IAIModelResolver.
        var hasModelOverride = !string.IsNullOrWhiteSpace(model);

        var request = new AIChatRequest
        {
            Messages =
            [
                new AIChatMessage("system", systemPrompt),
                new AIChatMessage("user", userMessage)
            ],
            Model = hasModelOverride ? model : null,
            Scenario = hasModelOverride ? null : scenario
        };

        var label = hasModelOverride ? model! : $"scenario:{scenario}";

        AIChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LLM call failed for {ModelOrScenario}", label);
            return null;
        }

        var text = response.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("LLM returned empty response for {ModelOrScenario}", label);
            return null;
        }

        var json = ExtractOutputJson(text);
        if (json is null)
        {
            logger.LogWarning("Could not extract <Output> JSON from LLM response. Raw text: {RawText}", text[..Math.Min(text.Length, 500)]);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize LLM response as {Type}. JSON: {Json}", typeof(T).Name, json);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<WordImagePromptPlan?> GenerateAlphabetPlanAsync(WordImageInput input, CancellationToken ct = default)
    {
        var plannerPrompt = string.Join("\n",
        [
            "You are planning semantic details for an English-learning alphabet flashcard illustration.",
            "The application has already decided the card type. You must not change the card type.",
            "Your output is not the final image prompt. Return only a compact JSON object wrapped in <Output></Output>.",
            string.Empty,
            "Hard rules:",
            "1. Prioritize classroom clarity over beauty.",
            "2. Keep the scene minimal, centered, and easy for children to understand.",
            "3. The target letter should be the central visible text element of the flashcard.",
            "4. If people are needed, depict ordinary modern Chinese individuals with plain contemporary clothing. People must wear appropriate footwear (shoes or socks) — never barefoot.",
            "5. If people appear, show the full body from head to toe — including complete hands and feet. Never crop at ankles, wrists, waist, or neck.",
            "6. Do not add decorative props, scenic filler, glamour styling, poster composition, or political elements.",
            "7. Keep every field short and concrete.",
            string.Empty,
            "Return JSON with this schema:",
            "{",
            "  \"mainSubject\": \"primary thing the image should show\",",
            "  \"supportingVisual\": \"optional supporting object, empty string if unnecessary\",",
            "  \"sceneSetting\": \"optional minimal setting, empty string if unnecessary\",",
            "  \"backgroundHint\": \"optional background hint, empty string if unnecessary\",",
            "  \"overlayText\": \"exact target letter\",",
            "  \"allowVisibleText\": true,",
            "  \"reserveTextOverlayArea\": false,",
            "  \"negativeElements\": [\"short item\", \"short item\"]",
            "}",
            string.Empty,
            "- Output only <Output>{json}</Output>."
        ]);

        var payload = new
        {
            input.RawInput,
            input.TargetText,
            input.MeaningHint,
            CardType = input.ContentType
        };

        var plan = await SendAsJsonAsync<WordImagePromptPlan>(
            plannerPrompt,
            JsonSerializer.Serialize(payload, JsonOptions),
            ResolveModel(),
            ct).ConfigureAwait(false);

        if (plan is null) return null;

        plan.AllowVisibleText = true;
        plan.ReserveTextOverlayArea = false;
        plan.OverlayText = input.TargetText.Trim();
        plan.NegativeElements = plan.NegativeElements?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return plan;
    }

    /// <inheritdoc />
    public async Task<WordImageVisualPlan?> GenerateVisualPlanAsync(WordImageInput input, CancellationToken ct = default)
    {
        var plannerPrompt = string.Join(Environment.NewLine,
        [
            "You are planning semantic visual details for an educational illustration.",
            "The final image will contain ZERO visible text — no letters, words, numbers, punctuation, captions, titles, labels, signs, or readable markings.",
            "Your job is to translate the language meaning into a complete purely visual event.",
            "The application has already decided the card type. You must not change the card type.",
            "Your output is not the final image prompt. Return only a compact JSON object wrapped in <Output></Output>.",
            string.Empty,
            "Hard rules:",
            "1. Prioritize semantic clarity for children over beauty.",
            "2. For Word cards, show the target meaning as one clear central subject with minimal context.",
            "3. For Phrase and Sentence cards, show a complete everyday situation, not just a single static pose.",
            "4. Describe only visual elements — no mention of text, letters, writing, captions, labels, typography, or readable symbols.",
            "5. If people are needed, depict ordinary modern Chinese individuals with plain contemporary clothing.",
            "   - For Chinese children: use natural black or deep brown hair and Asian facial features.",
            "   - People must wear appropriate footwear: sneakers or closed-toe shoes in school/public scenes, socks or slippers in home scenes. Never barefoot.",
            "6. Compose the image as a medium-wide shot or full-body shot. Keep heads, hands, arms, legs, and feet fully inside the frame when visible. Leave clear safe margins around all important people and objects.",
            "7. Scene setting should describe a simple recognizable environment that matches the meaning, such as a classroom, home, kitchen, park, playground, street, shop, bus stop, school hallway, or library.",
            "8. The setting must be recognizable within one second using 2 to 4 simple text-free environmental cues.",
            "9. Educational settings like classrooms are allowed and encouraged when the meaning involves learning, school, classroom behavior, or study. Use desks, chairs, windows, and a blank board as cues.",
            "10. Objects that could contain text, such as boards, books, notebooks, worksheets, posters, signs, menus, screens, or labels, may appear only when useful for the scene, and they must be completely blank with no visible writing.",
            "11. For negative or prohibitive sentences such as 'Don't X', 'Do not X', 'No X', or 'Never X', the image must show BOTH:",
            "    a) the forbidden action X clearly recognizable;",
            "    b) a non-text warning, stopping, or disapproval cue.",
            "    Do not represent a prohibition only with a raised hand, angry face, or empty stop gesture. The forbidden action itself must be visible.",
            "12. For safety-warning sentences such as 'Be careful', 'Watch out', or 'Look out', include a mild safety reason in the scene, such as being close to a desk, chair, step, wet floor, road edge, or everyday obstacle. Keep it safe and child-friendly: no injury, no falling, no accident.",
            "13. Avoid decorative filler, glamour styling, poster composition, political elements, flags, maps, national emblems, military symbols, weapons, religious symbols, or controversial imagery.",
            "14. Keep every field short, concrete, and visual.",
            string.Empty,
            "Return JSON with this schema:",
            "{",
            "  \"visualMeaning\": \"one sentence describing the complete visual event\",",
            "  \"sentenceIntent\": \"statement | action | warning | prohibition | question | emotion | other\",",
            "  \"mainSubject\": \"primary thing the image should show\",",
            "  \"primaryActor\": \"main actor, empty string if unnecessary\",",
            "  \"secondaryActor\": \"supporting actor, empty string if unnecessary\",",
            "  \"requiredAction\": \"required visible action, empty string if unnecessary\",",
            "  \"prohibitedAction\": \"forbidden action for negative/prohibition sentences, empty string if none\",",
            "  \"warningCue\": \"non-text warning or stopping cue, empty string if unnecessary\",",
            "  \"safetyCue\": \"mild safety reason, empty string if unnecessary\",",
            "  \"supportingVisual\": \"optional supporting object, empty string if unnecessary\",",
            "  \"actionOrGesture\": \"main gesture or action, empty string if unnecessary\",",
            "  \"sceneSetting\": \"simple recognizable setting, empty string only if no setting is useful\",",
            "  \"settingCues\": [\"2 to 4 simple text-free environmental cues\"],",
            "  \"backgroundHint\": \"brief background hint, empty string if unnecessary\",",
            "  \"mustShow\": [\"required visible evidence\", \"required visible evidence\"],",
            "  \"mustNotShow\": [\"short item\", \"short item\"],",
            "  \"negativeElements\": [\"short item\", \"short item\"]",
            "}",
            string.Empty,
            "For the sentence 'Be careful! Don't run in the classroom.', a good plan must include:",
            "- a child clearly running or about to run between classroom desks;",
            "- another person giving a non-text warning or stop gesture;",
            "- a recognizable classroom with desks, chairs, windows, and a blank board;",
            "- a mild safety cue such as the running child being near desks or chairs;",
            "- no injury, no falling, no accident, no visible text.",
            string.Empty,
            "- negativeElements must contain 0 to 8 short items.",
            "- mustShow must contain the visual evidence needed to understand the meaning.",
            "- Output only <Output>{json}</Output>."
        ]);

        var payload = new
        {
            Meaning = string.IsNullOrWhiteSpace(input.MeaningHint) ? input.TargetText : input.MeaningHint,
            CardType = input.ContentType
        };

        return await SendAsJsonAsync<WordImageVisualPlan>(
            plannerPrompt,
            JsonSerializer.Serialize(payload, JsonOptions),
            ResolveModel(),
            ct).ConfigureAwait(false);
    }

    // ── Helpers ──

    /// <summary>
    /// Extracts the JSON payload from an <c>&lt;Output&gt;...&lt;/Output&gt;</c> wrapper
    /// in the LLM response text.
    /// </summary>
    internal static string? ExtractOutputJson(string text)
    {
        var match = OutputTagRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Resolves the model identifier to use for visual plan generation.
    /// Returns <c>null</c> by default, which triggers scenario-based resolution
    /// via <c>AI:Models:Chat:{scenario}</c> in application configuration.
    /// Override in derived classes to return a specific model name for direct override.
    /// </summary>
    protected virtual string? ResolveModel() => null;

    [GeneratedRegex(@"<Output>\s*([\s\S]*?)\s*</Output>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutputTagRegex();
}
