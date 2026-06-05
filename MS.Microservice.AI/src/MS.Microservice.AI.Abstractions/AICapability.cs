namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Represents the distinct AI capabilities supported by the module.
/// Each value corresponds to a provider-neutral entry point with its own
/// request/response contracts, model resolution pipeline, and provider interface.
/// </summary>
public enum AICapability
{
    /// <summary>Chat / text completion.</summary>
    Chat = 1,

    /// <summary>Text-to-speech synthesis.</summary>
    Tts = 2,

    /// <summary>Automatic speech recognition / transcription.</summary>
    Asr = 3,

    /// <summary>Image generation from a text prompt.</summary>
    ImageGeneration = 4,

    /// <summary>Image editing (inpainting, background removal, etc.).</summary>
    ImageEdit = 5,
}