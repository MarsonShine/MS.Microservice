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
