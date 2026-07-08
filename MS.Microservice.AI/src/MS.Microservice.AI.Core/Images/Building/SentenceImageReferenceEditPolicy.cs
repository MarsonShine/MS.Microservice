using MS.Microservice.AI.Core.Images.Models;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Decides whether a grouped sentence should use reference-image editing.
/// Reference editing is powerful for continuity, but harmful for broad instruction lists
/// where each sentence may require a different action or location.
/// </summary>
public static class SentenceImageReferenceEditPolicy
{
    private static readonly HashSet<string> EligibleGroupTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "object_drill",
        "dialogue",
        "greeting",
        "self_introduction",
        "location_tour",
        "pre_assigned"
    };

    private static readonly HashSet<string> IneligibleGroupTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "single_sentence",
        "uncertain",
        "safety_rules",
        "safety_rule",
        "safety_sequence",
        "instructional_sequence",
        "instruction_sequence",
        "action_sequence",
        "activity_sequence",
        "exercise_sequence",
        "sports_safety",
        "play_safety"
    };

    public static bool ShouldUseReferenceEdit(VisualContextGroup? group)
    {
        if (group == null || group.RowIds.Count <= 1)
            return false;

        var groupType = group.GroupType?.Trim() ?? string.Empty;
        if (IneligibleGroupTypes.Contains(groupType))
            return false;

        if (EligibleGroupTypes.Contains(groupType))
            return true;

        if (group.Confidence < 0.8)
            return false;

        return group.Characters.Count > 0 &&
            !string.IsNullOrWhiteSpace(group.SceneSetting) &&
            HasStrongContinuityPolicy(group.ContinuityPolicy);
    }

    private static bool HasStrongContinuityPolicy(string? continuityPolicy)
    {
        if (string.IsNullOrWhiteSpace(continuityPolicy))
            return false;

        var text = continuityPolicy.ToLowerInvariant();
        return text.Contains("same character") ||
            text.Contains("same person") ||
            text.Contains("same setting") ||
            text.Contains("same scene") ||
            text.Contains("consistent clothing");
    }
}
