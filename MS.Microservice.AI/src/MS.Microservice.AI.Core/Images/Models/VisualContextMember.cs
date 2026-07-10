using System.Text.Json.Serialization;

namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Sentence-level visual guidance inside a shared scene context.
/// </summary>
public sealed class VisualContextMember
{
    [JsonPropertyName("rowId")]
    public long RowId { get; set; }

    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }

    [JsonPropertyName("sceneHint")]
    public string? SceneHint { get; set; }

    [JsonPropertyName("orderIndex")]
    public int OrderIndex { get; set; }

    /// <summary>
    /// The current sentence's primary visual subject. This must not include sibling-row targets.
    /// </summary>
    [JsonPropertyName("visualFocus")]
    public string? VisualFocus { get; set; }

    /// <summary>
    /// The visible action, reaction, or state for this specific sentence.
    /// </summary>
    [JsonPropertyName("visualAction")]
    public string? VisualAction { get; set; }

    /// <summary>
    /// Objects or details that are allowed to change for this row only.
    /// </summary>
    [JsonPropertyName("variableElements")]
    public List<string> VariableElements { get; set; } = [];

    /// <summary>
    /// Structured localized edit delta from the group's anchor reference row to this row.
    /// </summary>
    [JsonPropertyName("editDelta")]
    public SentenceImageEditDelta? EditDelta { get; set; }
}
