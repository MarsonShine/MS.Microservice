using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Pipeline;

/// <summary>
/// Validates that the visual plan satisfies semantic requirements derived from the input text.
/// Returns a list of human-readable issue descriptions. An empty list means the plan passes.
/// </summary>
public static class VisualPlanValidator
{
    public static List<string> Validate(WordImageInput input, WordImageVisualPlan plan)
    {
        var issues = new List<string>();
        var text = input.TargetText ?? string.Empty;

        if (input.ContentType is not (WordImageCardType.Sentence or WordImageCardType.Phrase))
            return issues;

        if (SentenceSemanticAnalyzer.IsProhibitive(text))
        {
            if (string.IsNullOrWhiteSpace(plan.ProhibitedAction) &&
                !PromptNormalizer.ContainsAny(plan.MustShow, "forbidden", "prohibited", "running", "run"))
                issues.Add("Missing visible forbidden/prohibited action.");

            if (string.IsNullOrWhiteSpace(plan.WarningCue) &&
                !PromptNormalizer.ContainsAny(plan.MustShow, "warning", "stopping", "stop"))
                issues.Add("Missing non-text warning or stopping cue.");
        }

        if (SentenceSemanticAnalyzer.IsCareful(text))
        {
            if (string.IsNullOrWhiteSpace(plan.SafetyCue) &&
                !PromptNormalizer.ContainsAny(plan.MustShow, "safety", "careful", "obstacle"))
                issues.Add("Missing mild safety reason for 'Be careful'.");
        }

        if (SentenceSemanticAnalyzer.MentionsClassroom(text))
        {
            if (string.IsNullOrWhiteSpace(plan.SceneSetting) ||
                !PromptNormalizer.ContainsAny([plan.SceneSetting], "classroom", "school"))
                issues.Add("Missing recognizable classroom setting.");

            if (plan.SettingCues == null || plan.SettingCues.Count < 2)
                issues.Add("Missing classroom environmental cues such as desks, chairs, windows, or blank board.");
        }

        if (SentenceSemanticAnalyzer.MentionsRunning(text))
        {
            if (string.IsNullOrWhiteSpace(plan.RequiredAction) &&
                !PromptNormalizer.ContainsAny(plan.MustShow, "run", "running"))
                issues.Add("Missing visible running action.");
        }

        return issues;
    }
}
