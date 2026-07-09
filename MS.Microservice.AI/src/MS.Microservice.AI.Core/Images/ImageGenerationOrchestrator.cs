using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images.Models;
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
/// Educational image generation orchestrator. Bridges the word-image prompt planning
/// pipeline with the framework's <see cref="IAIImageGenerationClient"/> for text-to-image
/// and an optional <see cref="IReferenceImageEditClient"/> for reference-image editing.
/// </summary>
/// <remarks>
/// <para>Typical usage:</para>
/// <code>
/// var result = await orchestrator.GenerateFromTextAsync("Be careful! Don't run in the classroom.");
/// </code>
/// </remarks>
public class ImageGenerationOrchestrator(
    WordImagePromptPipeline promptPipeline,
    IAIImageGenerationClient imageClient,
    ILogger<ImageGenerationOrchestrator> logger)
{
    private readonly WordImagePromptPipeline promptPipeline = promptPipeline;
    private readonly IAIImageGenerationClient imageClient = imageClient;
    private readonly ILogger<ImageGenerationOrchestrator> logger = logger;

    /// <summary>
    /// Optional reference-image edit client. When not registered (e.g. no Qwen adapter),
    /// reference-edit calls will throw a descriptive exception.
    /// </summary>
    public IReferenceImageEditClient? ReferenceEditClient { get; set; }

    /// <summary>
    /// Generates an educational image from raw text input (word, phrase, or sentence).
    /// The pipeline: raw text → LLM visual plan → safe prompt → image generation.
    /// </summary>
    public async Task<ImageGenerationResult> GenerateFromTextAsync(
        string wordText,
        AIImageGenerationRequest? generationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordText);

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

        var imageResponse = await imageClient
            .GenerateAsync(imageRequest, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Image generated for '{WordText}': {ImageCount} image(s) via {Provider}/{Model}",
            wordText, imageResponse.Images.Count, imageResponse.Provider, imageResponse.Model);

        return new ImageGenerationResult(richPrompt, safePrompt, imageResponse);
    }

    /// <summary>
    /// Generates (or reference-edits) an educational image from raw text input using a
    /// reference source image. Requires <see cref="ReferenceEditClient"/> to be set
    /// (registered via <c>AddQwen()</c> or equivalent adapter).
    /// When the pipeline produces a concrete edit delta, an edit call is issued;
    /// otherwise the source image is reused as-is.
    /// </summary>
    public async Task<ReferenceImageEditResult> GenerateFromTextWithReferenceAsync(
        string wordText,
        string referenceImageUrl,
        ImageEditGenerationOverrides? overrides = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordText);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceImageUrl);

        if (ReferenceEditClient is null)
        {
            throw new InvalidOperationException(
                "Reference image editing requires a provider adapter such as AddQwen(). Register IReferenceImageEditClient via DI.");
        }

        // Step 1: Build the reference-edit prompts
        string? richPrompt = null;
        string? safePrompt = null;

        try
        {
            (richPrompt, safePrompt) = await promptPipeline
                .GenerateReferenceEditPromptsAsync(wordText, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reference edit prompt planning failed for '{WordText}'.", wordText);
        }

        // Step 2: If no concrete visual delta, reuse source image
        if (string.IsNullOrWhiteSpace(safePrompt))
        {
            logger.LogInformation(
                "Reference edit SKIPPED for '{WordText}': no concrete visual delta. Reusing source image.",
                wordText);

            return new ReferenceImageEditResult(
                RichPrompt: richPrompt,
                SafePrompt: null,
                ImageResponse: new AIImageResponse
                {
                    Provider = "ReferenceImage",
                    Model = "reused-source",
                    Images = [new AIImageData { Url = referenceImageUrl }],
                },
                ReusedSourceImage: true,
                ReferenceImageUrl: referenceImageUrl);
        }

        // Step 3: Build and send the reference edit request
        var negativePrompt = promptPipeline.GenerateReferenceEditNegativePrompt(wordText);

        var editRequest = new ReferenceImageEditRequest
        {
            Prompt = safePrompt,
            ReferenceImageUrl = referenceImageUrl,
            NegativePrompt = negativePrompt,
            Model = overrides?.Model,
            Scenario = overrides?.Scenario,
            RequestId = overrides?.RequestId,
            Count = overrides?.Count,
            Size = overrides?.Size,
            Timeout = overrides?.Timeout,
        };

        var imageResponse = await ReferenceEditClient
            .EditReferenceAsync(editRequest, ct)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Reference edit completed for '{WordText}': {ImageCount} image(s) via {Provider}/{Model}",
            wordText, imageResponse.Images.Count, imageResponse.Provider, imageResponse.Model);

        return new ReferenceImageEditResult(
            RichPrompt: richPrompt,
            SafePrompt: safePrompt,
            ImageResponse: imageResponse,
            ReusedSourceImage: false,
            ReferenceImageUrl: referenceImageUrl);
    }

    /// <summary>
    /// Generates only the prompts (rich + safe) without calling the image generation API.
    /// </summary>
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
