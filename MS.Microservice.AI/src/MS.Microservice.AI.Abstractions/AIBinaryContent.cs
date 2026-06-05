namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// Wraps raw binary payload data together with optional MIME type and file name
/// metadata. Used for audio inputs (ASR), image inputs (edit), and audio/image
/// outputs (TTS, image generation).
/// </summary>
public sealed record AIBinaryContent
{
    /// <summary>Raw byte content (audio samples, image bytes, etc.).</summary>
    public required byte[] Content { get; init; }

    /// <summary>MIME type, e.g. <c>audio/wav</c> or <c>image/png</c>.</summary>
    public string? ContentType { get; init; }

    /// <summary>Suggested file name, e.g. <c>speech.mp3</c>.</summary>
    public string? FileName { get; init; }
}