using MS.Microservice.AI.Core.Images.Analysis;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;
using System.Text.RegularExpressions;

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
            parts.Add("Use only the objects needed to understand the current sentence.");

            if (input.ContentType == WordImageCardType.Word)
                BuildWordCard(parts, input, plan);
            else
                BuildEventCard(parts, input, plan);

            AppendSentenceImageControlContext(parts, input);
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

    public static string BuildReferenceEditPrompt(WordImageInput input, WordImagePromptPlan? _)
    {
        var sourceLocator = BuildSourceLocatorContext(input);
        var changeWhitelist = BuildChangeWhitelist(input);

        if (string.IsNullOrWhiteSpace(changeWhitelist))
        {
            // No concrete visual delta identified. Do NOT call the image model.
            // Even "keep unchanged" prompts cause a full re-encode that darkens and
            // alters watercolor texture, brightness, and contrast.
            return string.Empty;
        }

        var parts = new List<string>
        {
            "Use the SOURCE IMAGE as the base canvas.",
            "Perform one small localized semantic edit in the named target area.",
            "Keep all other areas pixel-faithful to the SOURCE IMAGE.",
            "Keep the same composition, crop, camera angle, object layout, background, character appearance, line style, texture level, color palette, brightness, exposure, contrast, saturation, and lighting."
        };

        if (!string.IsNullOrWhiteSpace(sourceLocator))
        {
            parts.Add($"Target area locator: {sourceLocator}.");
        }

        parts.Add($"Named target edit: {changeWhitelist}.");
        parts.Add("Blend the edited area naturally with the surrounding original pixels.");

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static string BuildReferenceEditNegativePrompt(WordImageInput input)
    {
        var items = new List<string>
        {
            "style transfer",
            "full redraw",
            "new composition",
            "different camera angle",
            "changed background",
            "changed character identity",
            "darker image",
            "over-saturated colors",
            "higher contrast",
            "dramatic shadows",
            "color grading",
            "vignette",
            "glow",
            "haze",
            "extra objects",
            "unrequested changes"
        };

        var requestedChangeText = string.Join(" ", new[]
        {
            BuildChangeWhitelist(input),
            BuildTargetStateContext(input)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!requestedChangeText.Contains("arrow", StringComparison.OrdinalIgnoreCase))
            items.Add("arrows, pointer marks");

        if (!requestedChangeText.Contains("text", StringComparison.OrdinalIgnoreCase) &&
            !requestedChangeText.Contains("label", StringComparison.OrdinalIgnoreCase))
            items.Add("new text, captions, labels");

        return string.Join(", ", items);
    }

    private static void AppendSentenceImageControlContext(List<string> parts, WordImageInput input)
    {
        var context = BuildSentenceImageControlContext(input);
        if (string.IsNullOrWhiteSpace(context))
            return;

        parts.Add($"Sentence image control context: {context}.");
    }

    private static IEnumerable<string> ExtractControlClauses(WordImageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.MeaningHint) || !ContainsSentenceControlMarker(input.MeaningHint))
            return [];

        return Regex.Split(input.MeaningHint, @"(?<=\.)\s+|;\s+")
            .Select(NormalizeControlClause)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .Where(clause => !ContainsNegativeControlLanguage(clause))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? BuildChangeWhitelist(WordImageInput input)
    {
        var clauses = ExtractControlClauses(input)
            .Where(IsExplicitEditInstructionClause)
            .Where(clause => !IsUnsafeBroadEditClause(clause))
            .Where(HasConcreteVisualTarget)
            .Take(4)
            .ToList();

        return JoinLimited(clauses, 600);
    }

    private static bool IsUnsafeBroadEditClause(string clause)
    {
        var markers = new[]
        {
            "Remove or soften reference-row details",
            "reference-row details",
            "Prompt branch",
            "Depict ",
            "Current target sentence",
            "Current sentence image only",
            "Shared context type",
            "Use only the objects needed"
        };

        return markers.Any(marker =>
            clause.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasConcreteVisualTarget(string clause)
    {
        // Must contain a replacement pattern like "replace X with Y" or "add X to Y"
        if (Regex.IsMatch(clause, @"\b(replace|revise|update|change)\b.+\b(with|to|into|as)\b", RegexOptions.IgnoreCase))
            return true;

        // Or an add/remove with at least 3 characters of target description
        if (Regex.IsMatch(clause, @"\b(add|remove)\b\s+.{3,}", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static string? BuildSourceLocatorContext(WordImageInput input)
    {
        var clauses = ExtractControlClauses(input)
            .Where(IsSourceLocatorClause)
            .Take(4)
            .ToList();

        return JoinLimited(clauses, 500);
    }

    private static string? BuildTargetStateContext(WordImageInput input)
    {
        var clauses = ExtractControlClauses(input)
            .Where(IsTargetStateClause)
            .Take(5)
            .ToList();

        return JoinLimited(clauses, 600);
    }

    private static string? BuildSentenceImageControlContext(WordImageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.MeaningHint) || !ContainsSentenceControlMarker(input.MeaningHint))
            return null;

        var clauses = Regex.Split(input.MeaningHint, @"(?<=\.)\s+|;\s+")
            .Select(NormalizeControlClause)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .Where(IsUsefulControlClause)
            .Where(clause => !ContainsNegativeControlLanguage(clause))
            .Where(clause => !clause.Contains("board", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();

        if (clauses.Count == 0)
            return null;

        var context = string.Join(". ", clauses);
        if (context.Length > 1400)
            context = context[..1400].TrimEnd(' ', '.', ',', ';') + ".";

        return context;
    }

    private static bool ContainsSentenceControlMarker(string hint)
    {
        return hint.Contains("IMAGE EDIT DELTA", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("Current sentence image only", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("Prompt branch", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("Shared context type", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeControlClause(string clause)
    {
        var normalized = Regex.Replace(clause, @"\s+", " ").Trim();
        normalized = normalized.Trim('(', ')', '.', ' ');
        return normalized;
    }

    private static bool IsUsefulControlClause(string clause)
    {
        var usefulMarkers = new[]
        {
            "Current sentence image only",
            "Shared context type",
            "Stable scene",
            "Stable characters",
            "Current speaker",
            "Current sentence visual focus",
            "Current sentence visual action",
            "Sentence-specific variable elements",
            "Row illustration hint",
            "IMAGE EDIT DELTA",
            "Preserve the exact same",
            "Preserve the same people",
            "Reference scene to preserve",
            "Reference image currently illustrates",
            "Reference row visual focus",
            "Reference row visual action",
            "Reference row variable elements",
            "Current target sentence",
            "Current target visual focus",
            "Current target visual action",
            "Current row variable elements",
            "Replace or revise only",
            "Only add or update",
            "Only adjust the visible state",
            "Remove or soften reference-row details",
            "Prompt branch target-object-focus",
            "Depict ",
            "red arrow pointer",
            "Prompt branch sparse-scene",
            "Sparse-scene composition",
            "Use one primary action",
            "Use only props",
            "For playground scenes"
        };

        return usefulMarkers.Any(marker => clause.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExplicitEditInstructionClause(string clause)
    {
        var markers = new[]
        {
            "Replace or revise only",
            "Only add or update",
            "Only adjust the visible state",
            "Change only",
            "Replace ",
            "Revise ",
            "Update ",
            "Add ",
            "Remove "
        };

        return markers.Any(marker => clause.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourceLocatorClause(string clause)
    {
        var markers = new[]
        {
            "Reference image currently illustrates",
            "Reference row visual focus",
            "Reference row visual action",
            "Reference row variable elements"
        };

        return markers.Any(marker => clause.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTargetStateClause(string clause)
    {
        var markers = new[]
        {
            "Current target visual focus",
            "Current target visual action",
            "Current row variable elements",
            "Current sentence visual focus",
            "Current sentence visual action",
            "Sentence-specific variable elements"
        };

        return markers.Any(marker => clause.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsNegativeControlLanguage(string clause)
    {
        return Regex.IsMatch(
            clause,
            @"\b(do not|don't|no|never|without|avoid|must not|should not|forbidden|prohibited|absent unless)\b",
            RegexOptions.IgnoreCase);
    }

    private static string? JoinLimited(List<string> clauses, int maxLength)
    {
        if (clauses.Count == 0)
            return null;

        var text = string.Join(". ", clauses);
        if (text.Length > maxLength)
            text = text[..maxLength].TrimEnd(' ', '.', ',', ';') + ".";

        return text;
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
                    .Take(2)
                    .ToList();
                if (cues.Count > 0)
                    parts.Add($"Recognizable details include {string.Join(", ", cues)}.");
            }
        }

        // Supporting visual
        var cleanSupport = PromptSanitizer.Clean(plan?.SupportingVisual);
        if (!string.IsNullOrWhiteSpace(cleanSupport))
            parts.Add($"Additional detail: {cleanSupport}.");
    }
}
