namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Result of batch prompt generation for a single row.
/// </summary>
public sealed class WordImagePromptResult
{
    public long RowId { get; set; }
    public string SceneGroupId { get; set; } = string.Empty;
    public string? RichPrompt { get; set; }
    public string? SafePrompt { get; set; }
    public double ContextConfidence { get; set; }
    public string? Speaker { get; set; }
}
