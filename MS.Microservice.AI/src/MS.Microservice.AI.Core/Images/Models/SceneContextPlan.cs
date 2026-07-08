namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Fixed visual context for a scene group — shared across all sentences in the group.
/// Generated once per group, then each sentence generates its own line-level prompt
/// that references this shared context.
/// </summary>
public sealed class SceneContextPlan
{
    public string SceneGroupId { get; set; } = string.Empty;

    /// <summary>Visual setting description (e.g., "bright classroom with blank board").</summary>
    public string SceneSetting { get; set; } = string.Empty;

    /// <summary>Stable character profiles (appearance must be identical across images).</summary>
    public List<CharacterProfile> Characters { get; set; } = [];

    /// <summary>Shared background description.</summary>
    public string SharedBackground { get; set; } = string.Empty;

    /// <summary>Art style and composition constraints.</summary>
    public string Style { get; set; } = "same children's storybook style, medium-wide horizontal 4:3 composition";

    /// <summary>Continuity rules for the group.</summary>
    public string ContinuityPolicy { get; set; } = string.Empty;

    /// <summary>Builds the shared context injection string for line prompts.</summary>
    public string BuildContextPrefix()
    {
        var parts = new List<string> { $"Use scene context {SceneGroupId}." };

        if (!string.IsNullOrWhiteSpace(SceneSetting))
            parts.Add($"Setting: {SceneSetting}.");

        if (Characters.Count > 0)
        {
            var charDescs = Characters.Select(c => $"{c.Name} ({c.Appearance})");
            parts.Add($"Characters: {string.Join("; ", charDescs)}.");
        }

        if (!string.IsNullOrWhiteSpace(SharedBackground))
            parts.Add($"Background: {SharedBackground}.");

        parts.Add($"Style: {Style}.");
        parts.Add("Keep the same characters, same appearances, same clothing, same setting.");

        return string.Join(" ", parts);
    }
}
