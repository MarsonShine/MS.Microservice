using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core.Images.Building;
using MS.Microservice.AI.Core.Images.Models;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Orchestrates batch sentence-image generation with scene-grouping awareness
/// and reference-image edit continuity. Processes groups sequentially within
/// each group to ensure visual consistency, while eligible groups use
/// reference-image editing for smooth transitions.
/// </summary>
public class SentenceImageBatchOrchestrator
{
    private readonly ISceneGroupingAgent sceneGroupingAgent;
    private readonly ImageGenerationOrchestrator generationOrchestrator;
    private readonly ILogger<SentenceImageBatchOrchestrator> logger;

    public SentenceImageBatchOrchestrator(
        ISceneGroupingAgent sceneGroupingAgent,
        ImageGenerationOrchestrator generationOrchestrator,
        ILogger<SentenceImageBatchOrchestrator> logger)
    {
        this.sceneGroupingAgent = sceneGroupingAgent;
        this.generationOrchestrator = generationOrchestrator;
        this.logger = logger;
    }

    /// <summary>
    /// Generates images for a batch of rows, respecting scene grouping and
    /// applying reference-image editing for eligible groups.
    /// </summary>
    public async Task<IReadOnlyList<SentenceImageBatchGenerationResult>> GenerateBatchAsync(
        IReadOnlyList<WordImageRow> rows,
        AIImageGenerationRequest? generationOverrides = null,
        ImageEditGenerationOverrides? editOverrides = null,
        CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return [];

        // Step 1: Group rows into visual context groups
        var groupingResult = await sceneGroupingAgent.GroupAsync(rows, ct).ConfigureAwait(false);
        var groups = groupingResult.Groups;

        // Build lookup from row to group for confidence info, and order index for final sort
        var rowConfidence = new Dictionary<long, double>();
        var rowOrder = new Dictionary<long, int>();
        foreach (var row in rows)
        {
            rowOrder[row.RowId] = row.OrderIndex;
        }
        foreach (var group in groups)
        {
            foreach (var rowId in group.RowIds)
            {
                rowConfidence[rowId] = group.Confidence;
            }
        }

        var results = new List<SentenceImageBatchGenerationResult>();

        // Step 2: Process each group sequentially
        foreach (var group in groups)
        {
            var orderedRows = group.RowIds
                .Select(id => rows.FirstOrDefault(r => r.RowId == id))
                .Where(r => r is not null)
                .OrderBy(r => r!.OrderIndex)
                .ToList();

            if (orderedRows.Count == 0)
                continue;

            if (!SentenceImageReferenceEditPolicy.ShouldUseReferenceEdit(group))
            {
                // ── Ineligible group: generate each row independently ──
                foreach (var row in orderedRows)
                {
                    var member = group.FindMember(row!.RowId);
                    var sceneContext = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);
                    var inputText = $"{row!.English} {sceneContext}";

                    var genResult = await generationOrchestrator
                        .GenerateFromTextAsync(inputText, generationOverrides, ct)
                        .ConfigureAwait(false);

                    results.Add(new SentenceImageBatchGenerationResult
                    {
                        RowId = row.RowId,
                        SceneGroupId = group.GroupId,
                        RichPrompt = genResult.RichPrompt,
                        SafePrompt = genResult.SafePrompt,
                        ImageResponse = genResult.ImageResponse,
                        UsedReferenceEdit = false,
                        ReusedSourceImage = false,
                        ReferenceImageUrl = null,
                        ContextConfidence = rowConfidence.GetValueOrDefault(row.RowId, 1.0)
                    });
                }
            }
            else
            {
                // ── Eligible group: first row normal generate, subsequent rows reference-edit ──
                string? lastReferenceUrl = null;

                for (var i = 0; i < orderedRows.Count; i++)
                {
                    var row = orderedRows[i]!;
                    var member = group.FindMember(row.RowId);

                    if (i == 0)
                    {
                        // First row: normal text-to-image generation
                        var sceneContext = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);
                        var inputText = $"{row.English} {sceneContext}";

                        var genResult = await generationOrchestrator
                            .GenerateFromTextAsync(inputText, generationOverrides, ct)
                            .ConfigureAwait(false);

                        // Get the generated image URL to use as reference for subsequent rows
                        lastReferenceUrl = GetImageUrl(genResult.ImageResponse);

                        if (string.IsNullOrWhiteSpace(lastReferenceUrl))
                        {
                            logger.LogWarning(
                                "First generated image for group {GroupId} has no URL; later rows will fall back to independent generation.",
                                group.GroupId);
                        }

                        results.Add(new SentenceImageBatchGenerationResult
                        {
                            RowId = row.RowId,
                            SceneGroupId = group.GroupId,
                            RichPrompt = genResult.RichPrompt,
                            SafePrompt = genResult.SafePrompt,
                            ImageResponse = genResult.ImageResponse,
                            UsedReferenceEdit = false,
                            ReusedSourceImage = false,
                            ReferenceImageUrl = null,
                            ContextConfidence = rowConfidence.GetValueOrDefault(row.RowId, group.Confidence)
                        });
                    }
                    else
                    {
                        // Subsequent row: try reference-image edit via structured delta
                        var editDelta = member?.EditDelta;

                        if (string.IsNullOrWhiteSpace(lastReferenceUrl)
                            || editDelta == null
                            || !SentenceImageEditPromptBuilder.CanUseReferenceEdit(editDelta))
                        {
                            // Fallback: independent generation
                            logger.LogWarning(
                                "No eligible reference edit for row {RowId} in group {GroupId} (no URL, no delta, or delta ineligible); falling back to independent generation.",
                                row.RowId, group.GroupId);

                            var sceneContext = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);
                            var fallbackText = $"{row.English} {sceneContext}";
                            var fallbackResult = await generationOrchestrator
                                .GenerateFromTextAsync(fallbackText, generationOverrides, ct)
                                .ConfigureAwait(false);

                            results.Add(new SentenceImageBatchGenerationResult
                            {
                                RowId = row.RowId,
                                SceneGroupId = group.GroupId,
                                RichPrompt = fallbackResult.RichPrompt,
                                SafePrompt = fallbackResult.SafePrompt,
                                ImageResponse = fallbackResult.ImageResponse,
                                UsedReferenceEdit = false,
                                ReusedSourceImage = false,
                                ReferenceImageUrl = null,
                                ContextConfidence = rowConfidence.GetValueOrDefault(row.RowId, group.Confidence)
                            });
                            continue;
                        }

                        // Attempt reference edit via structured delta
                        ReferenceImageEditResult editResult;
                        try
                        {
                            editResult = await generationOrchestrator
                                .GenerateFromReferenceEditDeltaAsync(editDelta, lastReferenceUrl, editOverrides, ct)
                                .ConfigureAwait(false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            logger.LogWarning(ex,
                                "Reference edit not available for row {RowId} in group {GroupId}; falling back to independent generation.",
                                row.RowId, group.GroupId);

                            var sceneContext = SentenceImageContinuityPromptBuilder.BuildSceneContext(group, member);
                            var fallbackText = $"{row.English} {sceneContext}";
                            var fallbackResult = await generationOrchestrator
                                .GenerateFromTextAsync(fallbackText, generationOverrides, ct)
                                .ConfigureAwait(false);

                            results.Add(new SentenceImageBatchGenerationResult
                            {
                                RowId = row.RowId,
                                SceneGroupId = group.GroupId,
                                RichPrompt = fallbackResult.RichPrompt,
                                SafePrompt = fallbackResult.SafePrompt,
                                ImageResponse = fallbackResult.ImageResponse,
                                UsedReferenceEdit = false,
                                ReusedSourceImage = false,
                                ReferenceImageUrl = null,
                                ContextConfidence = rowConfidence.GetValueOrDefault(row.RowId, group.Confidence)
                            });
                            continue;
                        }

                        // Advance reference URL only if an actual edit occurred and produced a valid URL
                        if (!editResult.ReusedSourceImage)
                        {
                            var newImageUrl = GetImageUrl(editResult.ImageResponse);

                            if (!string.IsNullOrWhiteSpace(newImageUrl))
                            {
                                lastReferenceUrl = newImageUrl;
                            }
                        }

                        results.Add(new SentenceImageBatchGenerationResult
                        {
                            RowId = row.RowId,
                            SceneGroupId = group.GroupId,
                            RichPrompt = editResult.RichPrompt,
                            SafePrompt = editResult.SafePrompt,
                            ImageResponse = editResult.ImageResponse,
                            UsedReferenceEdit = !editResult.ReusedSourceImage,
                            ReusedSourceImage = editResult.ReusedSourceImage,
                            ReferenceImageUrl = editResult.ReferenceImageUrl,
                            ContextConfidence = rowConfidence.GetValueOrDefault(row.RowId, group.Confidence)
                        });
                    }
                }
            }
        }

        // Step 3: Return results in original row order using pre-built lookup
        return results
            .OrderBy(r => rowOrder.GetValueOrDefault(r.RowId, int.MaxValue))
            .ToList();
    }

    private static string? GetImageUrl(AIImageResponse response)
    {
        return response.Images.FirstOrDefault(image => !string.IsNullOrWhiteSpace(image.Url))?.Url;
    }
}
