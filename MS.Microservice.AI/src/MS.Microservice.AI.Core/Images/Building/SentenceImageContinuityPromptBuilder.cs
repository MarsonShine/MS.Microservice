using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Builds shared-scene and image-edit continuity instructions for grouped sentence images.
/// </summary>
public static class SentenceImageContinuityPromptBuilder
{
    public static string BuildSceneContext(VisualContextGroup group, VisualContextMember? member)
    {
        var parts = new List<string>
        {
            "Current sentence image only; do not combine objects or actions from other sentences in the same group"
        };

        if (group.RowIds.Count > 1 && !string.IsNullOrWhiteSpace(group.GroupType))
            parts.Add($"Shared context type: {group.GroupType.Replace('_', ' ')}");

        if (!string.IsNullOrWhiteSpace(group.SceneSetting))
            parts.Add($"Stable scene: {group.SceneSetting}");

        AddStableCharacters(parts, group);

        if (member != null && !string.IsNullOrWhiteSpace(member.Speaker))
            parts.Add($"Current speaker: {member.Speaker}");

        if (member != null && !string.IsNullOrWhiteSpace(member.VisualFocus))
            parts.Add($"Current sentence visual focus: {member.VisualFocus}");

        if (member != null && !string.IsNullOrWhiteSpace(member.VisualAction))
            parts.Add($"Current sentence visual action: {member.VisualAction}");

        if (member?.VariableElements.Count > 0)
            parts.Add($"Sentence-specific variable elements: {string.Join(", ", member.VariableElements)}");

        if (member != null && !string.IsNullOrWhiteSpace(member.SceneHint))
            parts.Add($"Row illustration hint: {member.SceneHint}");

        parts.Add("Keep the stable scene and characters consistent; only the current sentence focus may change");
        return Wrap(parts);
    }

    public static string BuildImageEditContext(
        VisualContextGroup group,
        SentenceImageReferenceContext reference,
        string currentSentenceText,
        VisualContextMember? member)
    {
        return BuildImageEditContextCore(group, reference, currentSentenceText, member);
    }

    public static string BuildImageEditContext(VisualContextGroup group, VisualContextMember? member)
    {
        return BuildImageEditContextCore(group, null, null, member);
    }

    private static string BuildImageEditContextCore(
        VisualContextGroup group,
        SentenceImageReferenceContext? reference,
        string? currentSentenceText,
        VisualContextMember? member)
    {
        var parts = new List<string>
        {
            "IMAGE EDIT DELTA: edit the provided reference image instead of generating a new composition",
            "Preserve the exact same camera angle, framing, spatial layout, background anchors, lighting, color palette, and storybook art style"
        };

        if (group.Characters.Count > 0)
        {
            parts.Add("Preserve the same people as the same identities with identical faces, hairstyles, clothing, body proportions, and positions unless the current sentence explicitly requires a small gesture change");
        }
        else
        {
            parts.Add("Do not add new people unless the current sentence explicitly requires them");
        }

        if (!string.IsNullOrWhiteSpace(group.SceneSetting))
            parts.Add($"Reference scene to preserve: {group.SceneSetting}");

        AddStableCharacters(parts, group);

        if (reference != null)
        {
            if (!string.IsNullOrWhiteSpace(reference.SentenceText))
                parts.Add($"Reference image currently illustrates: \"{reference.SentenceText}\"");

            if (!string.IsNullOrWhiteSpace(reference.VisualFocus))
                parts.Add($"Reference row visual focus: {reference.VisualFocus}");

            if (!string.IsNullOrWhiteSpace(reference.VisualAction))
                parts.Add($"Reference row visual action or state: {reference.VisualAction}");

            var referenceElements = FormatElements(reference.VariableElements);
            if (!string.IsNullOrWhiteSpace(referenceElements))
                parts.Add($"Reference row variable elements: {referenceElements}");
        }

        if (!string.IsNullOrWhiteSpace(currentSentenceText))
            parts.Add($"Current target sentence: \"{currentSentenceText}\"");

        if (member != null && !string.IsNullOrWhiteSpace(member.VisualFocus))
            parts.Add($"Current target visual focus: {member.VisualFocus}");

        if (member != null && !string.IsNullOrWhiteSpace(member.VisualAction))
            parts.Add($"Current target visual action or state: {member.VisualAction}");

        var currentElements = FormatElements(member?.VariableElements);
        if (!string.IsNullOrWhiteSpace(currentElements))
            parts.Add($"Current row variable elements allowed to change: {currentElements}");

        var previousElements = FormatElements(reference?.VariableElements);
        if (!string.IsNullOrWhiteSpace(previousElements) && !string.IsNullOrWhiteSpace(currentElements))
        {
            parts.Add($"Replace or revise only the previous row-specific elements ({previousElements}) into the current row-specific elements ({currentElements})");
        }
        else if (!string.IsNullOrWhiteSpace(currentElements))
        {
            parts.Add($"Only add or update the current row-specific elements ({currentElements})");
        }
        else
        {
            parts.Add("Only adjust the visible state required by the current sentence");
        }

        parts.Add("Remove or soften reference-row details that conflict with the current sentence, but keep all stable scene details unchanged");
        parts.Add("Do not redesign the stable scene, do not change character identities, and do not introduce objects or actions from other sentences");
        parts.Add("Do not add readable text, signs, labels, boards, captions, or speech bubbles unless explicitly named by the current sentence");
        return Wrap(parts);
    }

    private static string FormatElements(IEnumerable<string>? elements)
    {
        if (elements == null)
            return string.Empty;

        return string.Join(", ", elements
            .Where(element => !string.IsNullOrWhiteSpace(element))
            .Select(element => element.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddStableCharacters(List<string> parts, VisualContextGroup group)
    {
        if (group.Characters.Count == 0)
            return;

        var characters = group.Characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .Select(character => string.IsNullOrWhiteSpace(character.Appearance)
                ? character.Name
                : $"{character.Name}: {character.Appearance}");

        parts.Add($"Stable characters: {string.Join("; ", characters)}");
    }

    private static string Wrap(List<string> parts)
    {
        return $"({string.Join(". ", parts.Where(part => !string.IsNullOrWhiteSpace(part)))}.)";
    }
}
