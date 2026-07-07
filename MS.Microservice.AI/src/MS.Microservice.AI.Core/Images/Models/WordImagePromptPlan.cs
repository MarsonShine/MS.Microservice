namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// The final assembled plan that drives prompt building.
/// Merged from LLM output + deterministic enrichment + validation/repair.
/// </summary>
public sealed class WordImagePromptPlan
{
    public string? MainSubject { get; set; }
    public string? SupportingVisual { get; set; }
    public string? ActionOrGesture { get; set; }
    public string? SceneSetting { get; set; }
    public string? BackgroundHint { get; set; }
    public string? OverlayText { get; set; }
    public bool AllowVisibleText { get; set; }
    public bool ReserveTextOverlayArea { get; set; }
    public List<string>? NegativeElements { get; set; }

    // Semantic completeness fields
    public string? SentenceIntent { get; set; }
    public string? PrimaryActor { get; set; }
    public string? SecondaryActor { get; set; }
    public string? RequiredAction { get; set; }
    public string? ProhibitedAction { get; set; }
    public string? WarningCue { get; set; }
    public string? SafetyCue { get; set; }
    public List<string>? SettingCues { get; set; }
    public List<string>? MustShow { get; set; }
    public List<string>? MustNotShow { get; set; }
}
