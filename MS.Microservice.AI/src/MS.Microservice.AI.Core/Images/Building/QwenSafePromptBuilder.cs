using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Builds a Qwen-safe positive-only image prompt.
/// Contains zero negative language and zero sensitive words — designed to pass
/// DashScope/Qwen content filters that scan prompt text for keywords even in negation form.
/// </summary>
public static class QwenSafePromptBuilder
{
    public static string Build(WordImageInput input, WordImagePromptPlan? plan)
    {
        var parts = new List<string>();

        // Fixed style header
        parts.Add("A simple 4:3 horizontal illustration in bright cheerful children's storybook style with clean smooth lines and flat soft colors.");
        parts.Add("Completely text-free image with no Chinese characters, English letters, numbers, punctuation, or readable markings anywhere.");
        parts.Add("Characters have natural expressive eyes with clear irises, visible eye whites, and small highlights instead of tiny dot eyes or bean-like eyes.");


        if (input.ContentType == WordImageCardType.Alphabet)
        {
            BuildAlphabetCard(parts, input, plan);
        }
        else
        {
            parts.Add("All boards books signs and labels in the scene are completely blank and unmarked.");

            if (input.ContentType == WordImageCardType.Word)
                BuildWordCard(parts, input, plan);
            else
                BuildEventCard(parts, input, plan);
        }

        // Positive composition guidance
        if (input.ContentType != WordImageCardType.Alphabet)
        {
            parts.Add("Medium-wide balanced composition with all characters fully visible from head to toe.");
            parts.Add("Characters wear everyday clothing and appropriate shoes suitable for the scene.");
            parts.Add("Comfortable margins around all subjects, nothing touching the frame edges.");
        }

        parts.Add("Cheerful warm atmosphere with gentle daylight and soft fresh colors.");

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static void BuildAlphabetCard(List<string> parts, WordImageInput input, WordImagePromptPlan? plan)
    {
        parts.Add($"The capital letter {input.TargetText} displayed large and centered in bold sans-serif on a plain light pastel background.");
        var cleanSubject = PromptSanitizer.Clean(plan?.MainSubject);
        if (!string.IsNullOrWhiteSpace(cleanSubject))
            parts.Add($"Below the letter, a simple flat illustration of {cleanSubject}.");
        parts.Add("Clean centered composition with soft pastel border.");
    }

    private static void BuildWordCard(List<string> parts, WordImageInput input, WordImagePromptPlan? plan)
    {
        var meaning = !string.IsNullOrWhiteSpace(input.MeaningHint) ? input.MeaningHint : input.TargetText;
        parts.Add($"A clear visual representation of the concept {meaning}.");

        var cleanMain = PromptSanitizer.Clean(plan?.MainSubject);
        if (!string.IsNullOrWhiteSpace(cleanMain))
            parts.Add($"The main focus is {cleanMain}.");

        var cleanScene = PromptSanitizer.Clean(plan?.SceneSetting);
        if (!string.IsNullOrWhiteSpace(cleanScene))
            parts.Add($"Set in {cleanScene}.");

        var cleanSupport = PromptSanitizer.Clean(plan?.SupportingVisual);
        if (!string.IsNullOrWhiteSpace(cleanSupport))
            parts.Add($"Accompanied by {cleanSupport}.");
    }

    private static void BuildEventCard(List<string> parts, WordImageInput input, WordImagePromptPlan? plan)
    {
        // Main subject
        var cleanMain = PromptSanitizer.Clean(plan?.MainSubject);
        if (!string.IsNullOrWhiteSpace(cleanMain))
            parts.Add(cleanMain.TrimEnd('.') + ".");

        // Primary actor
        var cleanActor = PromptSanitizer.Clean(plan?.PrimaryActor);
        if (!string.IsNullOrWhiteSpace(cleanActor))
            parts.Add(cleanActor.TrimEnd('.') + ".");

        // Required action
        var cleanAction = PromptSanitizer.Clean(plan?.RequiredAction);
        if (!string.IsNullOrWhiteSpace(cleanAction))
            parts.Add(cleanAction.TrimEnd('.') + ".");

        // ProhibitedAction (shown positively, deduped from RequiredAction)
        var cleanProhibited = PromptSanitizer.Clean(plan?.ProhibitedAction);
        if (!string.IsNullOrWhiteSpace(cleanProhibited) &&
            !SentenceSemanticAnalyzer.HasSignificantOverlap(cleanProhibited, plan?.RequiredAction))
            parts.Add(cleanProhibited.TrimEnd('.') + ".");

        // Warning cue
        var cleanWarning = PromptSanitizer.Clean(plan?.WarningCue);
        if (!string.IsNullOrWhiteSpace(cleanWarning))
            parts.Add(cleanWarning.TrimEnd('.') + ".");

        // Safety cue
        var cleanSafety = PromptSanitizer.Clean(plan?.SafetyCue);
        if (!string.IsNullOrWhiteSpace(cleanSafety))
            parts.Add(cleanSafety.TrimEnd('.') + ".");

        // Secondary actor
        var cleanSecondary = PromptSanitizer.Clean(plan?.SecondaryActor);
        if (!string.IsNullOrWhiteSpace(cleanSecondary))
            parts.Add(cleanSecondary.TrimEnd('.') + ".");

        // Action or gesture
        var cleanGesture = PromptSanitizer.Clean(plan?.ActionOrGesture);
        if (!string.IsNullOrWhiteSpace(cleanGesture))
            parts.Add(cleanGesture.TrimEnd('.') + ".");

        // Scene setting
        var cleanScene = PromptSanitizer.Clean(plan?.SceneSetting);
        if (!string.IsNullOrWhiteSpace(cleanScene))
        {
            parts.Add($"The scene takes place in {cleanScene}.");
            if (plan?.SettingCues?.Count > 0)
            {
                var cues = plan.SettingCues
                    .Select(c => PromptSanitizer.Clean(c))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Take(2) // Limit to at most 2 cues in the safe prompt to prevent visual clutter
                    .ToList();
                if (cues.Count > 0)
                    parts.Add($"Key background details include {string.Join(", ", cues)}.");
            }
        }

        // Supporting visual
        var cleanSupport = PromptSanitizer.Clean(plan?.SupportingVisual);
        if (!string.IsNullOrWhiteSpace(cleanSupport))
            parts.Add($"Additional detail: {cleanSupport}.");
    }
}
