using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Pipeline;

/// <summary>
/// Applies deterministic rules to enrich the LLM-generated visual plan
/// before it passes through validation and prompt building.
/// </summary>
public static class VisualPlanEnricher
{
    public static void Enrich(WordImageInput input, WordImageVisualPlan plan)
    {
        var text = input.TargetText ?? string.Empty;

        plan.MustShow ??= [];
        plan.MustNotShow ??= [];
        plan.SettingCues ??= [];

        if (input.ContentType is not (WordImageCardType.Sentence or WordImageCardType.Phrase))
            return;

        if (SentenceSemanticAnalyzer.IsProhibitive(text))
        {
            PromptNormalizer.AddDistinct(plan.MustShow, "the forbidden action itself must be clearly visible");
            PromptNormalizer.AddDistinct(plan.MustShow, "a clear non-text warning or stopping cue");
            plan.SentenceIntent = string.IsNullOrWhiteSpace(plan.SentenceIntent) ? "prohibition" : plan.SentenceIntent;
        }

        if (SentenceSemanticAnalyzer.IsCareful(text))
        {
            PromptNormalizer.AddDistinct(plan.MustShow, "a mild safety reason that explains why someone should be careful");
            PromptNormalizer.AddDistinct(plan.MustNotShow, "injury");
            PromptNormalizer.AddDistinct(plan.MustNotShow, "falling accident");
            PromptNormalizer.AddDistinct(plan.MustNotShow, "frightening danger");
            plan.SafetyCue = string.IsNullOrWhiteSpace(plan.SafetyCue)
                ? "near an everyday obstacle, but safe and child-friendly"
                : plan.SafetyCue;
        }

        if (SentenceSemanticAnalyzer.MentionsRunning(text))
        {
            plan.RequiredAction = string.IsNullOrWhiteSpace(plan.RequiredAction)
                ? "a child running or about to run"
                : plan.RequiredAction;

            if (SentenceSemanticAnalyzer.IsProhibitive(text))
                plan.ProhibitedAction = string.IsNullOrWhiteSpace(plan.ProhibitedAction) ? "running" : plan.ProhibitedAction;

            PromptNormalizer.AddDistinct(plan.MustShow, "a child clearly running or about to run, with a visible running pose");
        }

        if (SentenceSemanticAnalyzer.MentionsClassroom(text))
        {
            plan.SceneSetting = string.IsNullOrWhiteSpace(plan.SceneSetting) ? "a bright classroom" : plan.SceneSetting;
            PromptNormalizer.AddDistinct(plan.SettingCues, "desks");
            PromptNormalizer.AddDistinct(plan.SettingCues, "chairs");
            PromptNormalizer.AddDistinct(plan.SettingCues, "windows");
            PromptNormalizer.AddDistinct(plan.SettingCues, "blank board");
            PromptNormalizer.AddDistinct(plan.MustShow, "a recognizable classroom environment");
        }

        PromptNormalizer.NormalizeList(plan.MustShow, 8);
        PromptNormalizer.NormalizeList(plan.MustNotShow, 8);
        PromptNormalizer.NormalizeList(plan.SettingCues, 4);
    }
}
