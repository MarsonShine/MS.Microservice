namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Parsed input for word image prompt generation.
/// </summary>
public sealed record WordImageInput(string RawInput, string TargetText, string? MeaningHint, string ContentType);
