using MS.Microservice.AI.Abstractions;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// The complete result of an end-to-end image generation from raw educational text.
/// </summary>
/// <param name="RichPrompt">The full constraint-heavy prompt (for DB storage / tracing).</param>
/// <param name="SafePrompt">The Qwen-safe positive-only prompt (for actual generation).</param>
/// <param name="ImageResponse">The generated image(s) from the provider.</param>
public sealed record ImageGenerationResult(
    string? RichPrompt,
    string? SafePrompt,
    AIImageResponse ImageResponse);

/// <summary>
/// Bridges the word-image prompt planning pipeline with the framework's
/// <see cref="IAIImageGenerationClient"/> to provide a single-call,
/// end-to-end educational image generation service.
/// </summary>
/// <remarks>
/// <para>Typical usage:</para>
/// <code>
/// var result = await orchestrator.GenerateFromTextAsync("Be careful! Don't run in the classroom.");
/// // result.RichPrompt → store in DB for traceability
/// // result.SafePrompt → the prompt actually sent to the image provider
/// // result.ImageResponse.Images → the generated images
/// </code>
/// </remarks>
/// <remarks>
/// Initializes a new instance of <see cref="ImageGenerationOrchestrator"/>.
/// </remarks>
/// <param name="promptPipeline">The prompt planning pipeline.</param>
/// <param name="imageClient">The image generation client.</param>
/// <param name="logger">The logger instance.</param>
public class ImageGenerationOrchestrator(
    WordImagePromptPipeline promptPipeline,
    IAIImageGenerationClient imageClient,
    ILogger<ImageGenerationOrchestrator> logger)
{
    private readonly WordImagePromptPipeline promptPipeline = promptPipeline;
    private readonly IAIImageGenerationClient imageClient = imageClient;
    private readonly ILogger<ImageGenerationOrchestrator> logger = logger;

    /// <summary>
    /// Generates an educational image from raw text input (word, phrase, or sentence).
    /// The pipeline: raw text → LLM visual plan → safe prompt → image generation.
    /// </summary>
    /// <param name="wordText">
    /// The raw educational text, e.g. "apple", "Keep off the grass.", or
    /// "Be careful! Don't run in the classroom.".
    /// Supports optional meaning hints in parentheses: "apple (fruit)".
    /// </param>
    /// <param name="generationOverrides">
    /// Optional overrides for the image generation request (provider, model, size, quality, etc.).
    /// When <c>null</c>, the default scenario from <c>AI:Models:ImageGeneration</c> is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The complete result containing the rich prompt (for DB), safe prompt, and generated images.
    /// If prompt planning fails, falls back to using the raw text as the image prompt.
    /// </returns>
    public async Task<ImageGenerationResult> GenerateFromTextAsync(
        string wordText,
        AIImageGenerationRequest? generationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordText);

        // Step 1: Generate prompts through the planning pipeline
        string? richPrompt = null;
        string? safePrompt = null;

        try
        {
            (richPrompt, safePrompt) = await promptPipeline
                .GeneratePromptsAsync(wordText, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt planning failed for '{WordText}', falling back to raw text.", wordText);
        }

        // Step 2: Build the image generation request using the safe prompt
        var prompt = !string.IsNullOrWhiteSpace(safePrompt) ? safePrompt : wordText;

        var imageRequest = new AIImageGenerationRequest
        {
            Prompt = prompt,
            Provider = generationOverrides?.Provider,
            Model = generationOverrides?.Model,
            Scenario = generationOverrides?.Scenario,
            RequestId = generationOverrides?.RequestId,
            Count = generationOverrides?.Count,
            Size = generationOverrides?.Size,
            Quality = generationOverrides?.Quality,
            ResponseFormat = generationOverrides?.ResponseFormat,
            Timeout = generationOverrides?.Timeout,
            Metadata = generationOverrides?.Metadata,
        };

        // Step 3: Generate the image
        var imageResponse = await imageClient
            .GenerateAsync(imageRequest, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Image generated for '{WordText}': {ImageCount} image(s) via {Provider}/{Model}",
            wordText,
            imageResponse.Images.Count,
            imageResponse.Provider,
            imageResponse.Model);

        return new ImageGenerationResult(richPrompt, safePrompt, imageResponse);
    }

    /// <summary>
    /// Generates only the prompts (rich + safe) without calling the image generation API.
    /// Useful for pre-validation, cost estimation, or when image generation is deferred.
    /// </summary>
    /// <param name="wordText">The raw educational text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of (richPrompt, safePrompt). Either may be null on failure.</returns>
    public async Task<(string? RichPrompt, string? SafePrompt)> GeneratePromptsOnlyAsync(
        string wordText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordText);
        return await promptPipeline
            .GeneratePromptsAsync(wordText, cancellationToken)
            .ConfigureAwait(false);
    }
}
