using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images.Building;
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
/// <para>Reference edit usage:</para>
/// <code>
/// var result = await orchestrator.GenerateFromReferenceEditDeltaAsync(delta, referenceImageUrl);
/// </code>
/// </remarks>
public class ImageGenerationOrchestrator(
    WordImagePromptPipeline promptPipeline,
    IAIImageGenerationClient imageClient,
    IEnumerable<IReferenceImageEditClient> referenceEditClients,
    ILogger<ImageGenerationOrchestrator> logger)
{
    private readonly WordImagePromptPipeline promptPipeline = promptPipeline;
    private readonly IAIImageGenerationClient imageClient = imageClient;
    private readonly IReferenceImageEditClient? referenceEditClient = referenceEditClients.FirstOrDefault();
    private readonly ILogger<ImageGenerationOrchestrator> logger = logger;

    // Keep a backwards-compatible constructor for tests that don't inject IReferenceImageEditClient.
    public ImageGenerationOrchestrator(
        WordImagePromptPipeline promptPipeline,
        IAIImageGenerationClient imageClient,
        ILogger<ImageGenerationOrchestrator> logger)
        : this(promptPipeline, imageClient, [], logger)
    {
    }

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
    /// Generates a reference-image edit from a structured <see cref="SentenceImageEditDelta"/>.
    /// When the delta is not eligible for reference edit (low confidence, no concrete operations),
    /// the source image is reused as-is.
    /// </summary>
    /// <param name="delta">The structured edit delta produced by <see cref="SentenceEditDeltaAgent"/>.</param>
    /// <param name="referenceImageUrl">The publicly accessible URL of the source/reference image.</param>
    /// <param name="overrides">Optional generation overrides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The edit result, with <see cref="ReferenceImageEditResult.ReusedSourceImage"/> indicating
    /// whether the source was reused or an actual edit was performed.</returns>
    public async Task<ReferenceImageEditResult> GenerateFromReferenceEditDeltaAsync(
        SentenceImageEditDelta delta,
        string referenceImageUrl,
        ImageEditGenerationOverrides? overrides = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceImageUrl);

        if (!SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta))
        {
            logger.LogInformation(
                "Reference edit SKIPPED for row {RowId}: delta not eligible (confidence={Confidence}). Reusing source image.",
                delta.RowId, delta.Confidence);

            return new ReferenceImageEditResult(
                RichPrompt: null,
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

        if (referenceEditClient is null)
        {
            throw new InvalidOperationException(
                "Reference image editing requires a provider adapter such as AddQwen(). Register IReferenceImageEditClient via DI.");
        }

        var editPrompt = SentenceImageEditPromptBuilder.BuildPrompt(delta);

        var negativePrompt =
            "style transfer, full redraw, new composition, changed background, " +
            "changed character identity, darker image, over-saturated colors, " +
            "extra objects, unrequested changes";

        var editRequest = new ReferenceImageEditRequest
        {
            Prompt = editPrompt,
            ReferenceImageUrl = referenceImageUrl,
            NegativePrompt = negativePrompt,
            Model = overrides?.Model,
            Scenario = overrides?.Scenario,
            RequestId = overrides?.RequestId,
            Count = overrides?.Count,
            Size = overrides?.Size,
            Timeout = overrides?.Timeout,
        };

        try
        {
            var imageResponse = await referenceEditClient
                .EditReferenceAsync(editRequest, ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Reference edit completed for row {RowId}: {ImageCount} image(s) via {Provider}/{Model}",
                delta.RowId, imageResponse.Images.Count, imageResponse.Provider, imageResponse.Model);

            return new ReferenceImageEditResult(
                RichPrompt: null,
                SafePrompt: editPrompt,
                ImageResponse: imageResponse,
                ReusedSourceImage: false,
                ReferenceImageUrl: referenceImageUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reference edit failed for row {RowId}, reusing source image.", delta.RowId);

            return new ReferenceImageEditResult(
                RichPrompt: null,
                SafePrompt: null,
                ImageResponse: new AIImageResponse
                {
                    Provider = "ReferenceImage",
                    Model = "reused-source-fallback",
                    Images = [new AIImageData { Url = referenceImageUrl }],
                },
                ReusedSourceImage: true,
                ReferenceImageUrl: referenceImageUrl);
        }
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
