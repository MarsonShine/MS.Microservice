namespace MS.Microservice.Samples.EducationalImages.Models;

/// <summary>
/// Parsed input for word image prompt generation.
/// </summary>
public sealed record WordImageInput(string RawInput, string TargetText, string? MeaningHint, string ContentType);
