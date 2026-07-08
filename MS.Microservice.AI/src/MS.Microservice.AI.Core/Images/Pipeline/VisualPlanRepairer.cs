using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Pipeline;

/// <summary>
/// Attempts to repair a visual plan that failed validation by injecting
/// missing required elements based on the issue descriptions.
/// </summary>
public static class VisualPlanRepairer
{
    public static void Repair(WordImageInput input, WordImageVisualPlan plan, List<string> issues)
    {
        plan.MustShow ??= [];
        plan.SettingCues ??= [];
        plan.MustNotShow ??= [];

        foreach (var issue in issues)
        {
            if (issue.Contains("running", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("prohibited", StringComparison.OrdinalIgnoreCase))
            {
                plan.RequiredAction = string.IsNullOrWhiteSpace(plan.RequiredAction)
                    ? "a child clearly running or about to run"
                    : plan.RequiredAction;
                plan.ProhibitedAction = string.IsNullOrWhiteSpace(plan.ProhibitedAction) ? "running" : plan.ProhibitedAction;
                PromptNormalizer.AddDistinct(plan.MustShow, "a child clearly running or about to run, with a visible running pose");
            }

            if (issue.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("stopping", StringComparison.OrdinalIgnoreCase))
            {
                plan.WarningCue = string.IsNullOrWhiteSpace(plan.WarningCue)
                    ? "another person raises an open palm to warn or stop the running child"
                    : plan.WarningCue;
                PromptNormalizer.AddDistinct(plan.MustShow, "another person giving a clear non-text warning or stopping gesture");
            }

            if (issue.Contains("safety", StringComparison.OrdinalIgnoreCase))
            {
                plan.SafetyCue = string.IsNullOrWhiteSpace(plan.SafetyCue)
                    ? "the running child is close to desks or chairs, showing why running is unsafe"
                    : plan.SafetyCue;
                PromptNormalizer.AddDistinct(plan.MustShow, "a mild safety reason such as nearby desks or chairs, with no injury or accident");
                PromptNormalizer.AddDistinct(plan.MustNotShow, "injury");
                PromptNormalizer.AddDistinct(plan.MustNotShow, "falling");
                PromptNormalizer.AddDistinct(plan.MustNotShow, "accident");
            }

            if (issue.Contains("classroom", StringComparison.OrdinalIgnoreCase))
            {
                plan.SceneSetting = "a bright classroom";
                PromptNormalizer.AddDistinct(plan.SettingCues, "desks");
                PromptNormalizer.AddDistinct(plan.MustShow, "a recognizable classroom environment");
            }
        }

        PromptNormalizer.NormalizeList(plan.MustShow, 8);
        PromptNormalizer.NormalizeList(plan.MustNotShow, 8);
        PromptNormalizer.NormalizeList(plan.SettingCues, 1);
    }
}
