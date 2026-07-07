namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Raw visual plan returned by the LLM planner.
/// Enriched and validated before being merged into <see cref="WordImagePromptPlan"/>.
/// </summary>
public sealed class WordImageVisualPlan
{
    public string? VisualMeaning { get; set; }
    public string? MainSubject { get; set; }
    public string? SupportingVisual { get; set; }
    public string? ActionOrGesture { get; set; }
    public string? SceneSetting { get; set; }
    public string? BackgroundHint { get; set; }
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
