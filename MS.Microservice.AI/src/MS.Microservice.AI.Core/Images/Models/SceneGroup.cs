namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// A group of semantically related sentences that should share visual context
/// (same characters, same setting, consistent art style).
/// </summary>
public class SceneGroup
{
    public string GroupId { get; init; } = Guid.CreateVersion7().ToString("N")[..12];

    /// <summary>dialogue_pair | introduction_chain | parallel_locations | standalone</summary>
    public string GroupType { get; init; } = "standalone";

    /// <summary>Character names shared across the group.</summary>
    public List<string> CharacterNames { get; set; } = [];

    /// <summary>Shared setting description (e.g., "school hallway", "classroom circle").</summary>
    public string? SharedSetting { get; set; }

    /// <summary>How characters should look (consistent across group members).</summary>
    public string? CharacterStyleHint { get; set; }

    /// <summary>Builds a compact context string for injection into prompt input.</summary>
    public string BuildContextInjection()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(SharedSetting))
            parts.Add($"Scene: {SharedSetting}");

        if (CharacterNames.Count > 0)
            parts.Add($"Characters present: {string.Join(", ", CharacterNames)}");

        if (!string.IsNullOrWhiteSpace(CharacterStyleHint))
            parts.Add($"Character appearance: {CharacterStyleHint}");

        if (GroupType != "standalone")
            parts.Add($"This is part of a {GroupType.Replace('_', ' ')}");

        return parts.Count > 0 ? $"({string.Join(". ", parts)}.)" : string.Empty;
    }

    /// <summary>
    /// Builds a FROZEN master context string for multi-image consistency.
    /// This exact text is injected verbatim into every subsequent prompt in the group,
    /// ensuring identical character appearances, scene layout, and props.
    /// </summary>
    public string BuildFrozenMasterContext(string firstImageRichPrompt)
    {
        var parts = new List<string>
        {
            "FREEZE: Use EXACTLY the same visual elements as the master image for this group. Do not change any of these:"
        };

        if (!string.IsNullOrWhiteSpace(SharedSetting))
            parts.Add($"- Identical scene setting: {SharedSetting}");

        if (CharacterNames.Count > 0)
            parts.Add($"- Same characters in same positions: {string.Join(", ", CharacterNames)}");

        if (!string.IsNullOrWhiteSpace(CharacterStyleHint))
            parts.Add($"- Identical character appearances: {CharacterStyleHint}");

        parts.Add("- Same classroom/setting layout, same furniture positions, same props");
        parts.Add("- Same clothing, same hairstyles, same facial features, same body proportions");
        parts.Add("- Same lighting, same color palette, same art style");
        parts.Add("The ONLY difference should be the specific action or speech being depicted for this sentence.");

        return $"({string.Join(" ", parts)})";
    }
}
