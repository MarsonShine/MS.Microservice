using System.Text.Json.Serialization;

namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// How images in a group should be generated.
/// </summary>
public enum ImageReuseMode
{
    /// <summary>Each sentence gets its own independently generated image (legacy).</summary>
    GenerateAll = 0,

    /// <summary>Generate one image for the first sentence, reuse its URL for all others.</summary>
    ReuseFirst = 1,

    /// <summary>Each sentence gets its own image, but all share a frozen master context
    /// (exact same character descriptions, scene layout, clothing) generated from the first prompt.</summary>
    GenerateDistinctWithMaster = 2
}

/// <summary>
/// LLM-generated grouping of rows into a coherent visual scene.
/// </summary>
public sealed class VisualContextGroup
{
    public string GroupId { get; set; } = string.Empty;
    public List<long> RowIds { get; set; } = [];
    public string GroupType { get; set; } = "single_sentence";
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SceneSetting { get; set; } = string.Empty;
    public List<CharacterProfile> Characters { get; set; } = [];
    public List<string> SharedProps { get; set; } = [];
    public List<VisualContextMember> Members { get; set; } = [];
    public string ContinuityPolicy { get; set; } = string.Empty;

    /// <summary>How images should be generated for this group.</summary>
    [JsonIgnore]
    public ImageReuseMode ReuseMode { get; set; } = ImageReuseMode.GenerateAll;

    /// <summary>Raw reuse mode string from LLM.</summary>
    [JsonPropertyName("reuseMode")]
    public string? ReuseModeRaw
    {
        get => ReuseMode switch
        {
            ImageReuseMode.ReuseFirst => "reuse_first",
            ImageReuseMode.GenerateDistinctWithMaster => "generate_distinct",
            ImageReuseMode.GenerateAll => "generate_all",
            _ => "generate_all"
        };
        set => ReuseMode = ParseReuseMode(value);
    }

    public VisualContextMember? FindMember(long rowId)
    {
        return Members.FirstOrDefault(member => member.RowId == rowId);
    }

    private static ImageReuseMode ParseReuseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ImageReuseMode.GenerateAll;

        return raw.Trim().ToLowerInvariant() switch
        {
            "reuse_first" => ImageReuseMode.ReuseFirst,
            "generate_distinct" => ImageReuseMode.GenerateDistinctWithMaster,
            "generate_all" => ImageReuseMode.GenerateAll,
            _ => ImageReuseMode.GenerateAll
        };
    }
}
