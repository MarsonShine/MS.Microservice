namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Stable visual profile for a character that should appear consistently across images.
/// </summary>
public sealed class CharacterProfile
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "student";
    public string Appearance { get; set; } = string.Empty;
}
