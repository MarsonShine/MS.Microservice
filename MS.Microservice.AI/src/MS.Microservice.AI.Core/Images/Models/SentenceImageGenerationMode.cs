namespace MS.Microservice.AI.Core.Images.Models;

/// <summary>
/// Controls how sentence images are generated.
/// </summary>
public enum SentenceImageGenerationMode
{
    /// <summary>
    /// Generate each sentence independently through WordImagePromptPipeline.
    /// This is the pre-scene-grouping fallback path.
    /// </summary>
    LegacyPerSentence = 0,

    /// <summary>
    /// Use the scene grouping agent only for stable scene / character context,
    /// while generating one image per sentence.
    /// </summary>
    SceneContextPerSentence = 1
}
