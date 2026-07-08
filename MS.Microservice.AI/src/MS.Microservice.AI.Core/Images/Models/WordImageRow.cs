namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Input row for batch sentence image generation, sourced from Excel or DB.
/// </summary>
public sealed class WordImageRow
{
    public long RowId { get; set; }
    public string English { get; set; } = string.Empty;
    public string Chinese { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    /// <summary>Unit or catalogue identifier.</summary>
    public int? CatalogueId { get; set; }

    /// <summary>Pre-assigned scene group (from Excel). If set, skips AI grouping.</summary>
    public string? SceneGroupId { get; set; }

    /// <summary>Who is speaking (from Excel).</summary>
    public string? Speaker { get; set; }

    /// <summary>Who is being addressed (from Excel).</summary>
    public string? Addressee { get; set; }

    /// <summary>Visual hint for the scene (from Excel).</summary>
    public string? SceneHint { get; set; }
}
