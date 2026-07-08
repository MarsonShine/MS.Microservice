using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Pipeline;

public static class VisualPlanSceneSimplifier
{
    private static readonly string[] ClassroomCueKeywords =
    [
        "board", "blackboard", "whiteboard", "chalkboard", "desk", "classroom", "school", "teacher"
    ];

    private static readonly string[] ClutterCueKeywords =
    [
        "basketball", "football", "soccer", "ball", "swing", "seesaw", "climber", "sandbox",
        "poster", "sign", "label", "screen", "bookshelf", "shelf", "toy", "toys"
    ];

    private static readonly string[] PrimaryEnvironmentAnchors =
    [
        "slide", "road", "track", "bench", "water bottle", "playground floor", "open playground",
        "classroom desk", "desk", "chair", "window", "classroom"
    ];

    public static void Simplify(WordImageInput input, WordImageVisualPlan plan)
    {
        if (input.ContentType is not (WordImageCardType.Sentence or WordImageCardType.Phrase))
            return;

        var text = input.TargetText ?? string.Empty;
        var mentionsClassroom = SentenceSemanticAnalyzer.MentionsClassroom(text);

        plan.SettingCues = SimplifySettingCues(plan.SettingCues, mentionsClassroom);
        plan.MustShow = SimplifyMustShow(plan.MustShow, text);
        plan.SupportingVisual = SimplifySupportingVisual(plan.SupportingVisual, text, mentionsClassroom);

        plan.MustNotShow ??= [];
        if (!mentionsClassroom)
        {
            PromptNormalizer.AddDistinct(plan.MustNotShow, "blackboard");
            PromptNormalizer.AddDistinct(plan.MustNotShow, "whiteboard");
            PromptNormalizer.AddDistinct(plan.MustNotShow, "classroom board");
        }

        PromptNormalizer.AddDistinct(plan.MustNotShow, "extra playground equipment");
        PromptNormalizer.AddDistinct(plan.MustNotShow, "unrelated sports balls");
        PromptNormalizer.AddDistinct(plan.MustNotShow, "decorative props");

        PromptNormalizer.NormalizeList(plan.MustShow, 6);
        PromptNormalizer.NormalizeList(plan.MustNotShow, 12);
        PromptNormalizer.NormalizeList(plan.SettingCues, 1);
    }

    private static List<string> SimplifySettingCues(List<string>? settingCues, bool mentionsClassroom)
    {
        var cues = settingCues?
            .Where(cue => !string.IsNullOrWhiteSpace(cue))
            .Select(cue => cue.Trim())
            .Where(cue => mentionsClassroom || !ContainsAny(cue, ClassroomCueKeywords))
            .Where(cue => !ContainsAny(cue, ClutterCueKeywords))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var primaryCue = cues.FirstOrDefault(cue => ContainsAny(cue, PrimaryEnvironmentAnchors))
            ?? cues.FirstOrDefault();

        return string.IsNullOrWhiteSpace(primaryCue) ? [] : [primaryCue];
    }

    private static List<string> SimplifyMustShow(List<string>? mustShow, string text)
    {
        var values = mustShow?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => !ContainsAny(item, ClutterCueKeywords))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var lowerText = text.ToLowerInvariant();
        if (lowerText.Contains("play safely") || lowerText.Contains("be careful"))
        {
            values = values
                .Where(item => !ContainsAny(item, "multiple equipment", "several", "various"))
                .ToList();
        }

        return values;
    }

    private static string? SimplifySupportingVisual(string? supportingVisual, string text, bool mentionsClassroom)
    {
        if (string.IsNullOrWhiteSpace(supportingVisual))
            return supportingVisual;

        if (!mentionsClassroom && ContainsAny(supportingVisual, ClassroomCueKeywords))
            return string.Empty;

        if (ContainsAny(supportingVisual, ClutterCueKeywords) && !TextMentionsAny(text, supportingVisual))
            return string.Empty;

        return supportingVisual.Trim();
    }

    private static bool TextMentionsAny(string text, string phrase)
    {
        var lowerText = text.ToLowerInvariant();
        return phrase
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Length > 3 && lowerText.Contains(word.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
