namespace MS.Microservice.Samples.EducationalImages.Models;

/// <summary>
/// Describes the sentence-specific content already visible in a reference image.
/// </summary>
public sealed record SentenceImageReferenceContext(
    string ImageUrl,
    string SentenceText,
    string? VisualFocus,
    string? VisualAction,
    IReadOnlyList<string> VariableElements);
