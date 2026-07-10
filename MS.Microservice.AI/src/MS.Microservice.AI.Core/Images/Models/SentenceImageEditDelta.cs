using System.Text.Json.Serialization;

namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Structured edit plan for changing one grouped sentence image from the group's anchor source image.
/// </summary>
public sealed class SentenceImageEditDelta
{
    [JsonPropertyName("rowId")]
    public long RowId { get; set; }

    [JsonPropertyName("referenceRowId")]
    public long ReferenceRowId { get; set; }

    [JsonPropertyName("sourceLocator")]
    public string? SourceLocator { get; set; }

    [JsonPropertyName("operations")]
    public List<SentenceImageEditOperation> Operations { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    public bool HasConcreteOperations()
    {
        return Operations.Any(operation => operation.HasConcreteChange());
    }
}

public sealed class SentenceImageEditOperation
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "replace";

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("regionHint")]
    public string? RegionHint { get; set; }

    public bool HasConcreteChange()
    {
        var op = Operation?.Trim().ToLowerInvariant();
        return op switch
        {
            "replace" => !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To),
            "update" or "change" => !string.IsNullOrWhiteSpace(Target) && !string.IsNullOrWhiteSpace(To),
            "add" => !string.IsNullOrWhiteSpace(To) || !string.IsNullOrWhiteSpace(Target),
            "remove" => !string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(Target),
            _ => !string.IsNullOrWhiteSpace(Target) && (!string.IsNullOrWhiteSpace(To) || !string.IsNullOrWhiteSpace(From))
        };
    }
}
