using Microsoft.Extensions.Logging;
using MS.Microservice.AI.Core.Images.Models;
using MS.Microservice.Core.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.AI.Core.Images;

/// <summary>
/// Identifies structured localized edit deltas between rows in the same visual context group.
/// </summary>
public sealed class SentenceEditDeltaAgent
{
    private readonly IPlanGeneratorClient planClient;
    private readonly ILogger<SentenceEditDeltaAgent> logger;

    public SentenceEditDeltaAgent(IPlanGeneratorClient planClient, ILogger<SentenceEditDeltaAgent> logger)
    {
        this.planClient = planClient;
        this.logger = logger;
    }

    public async Task EnrichAsync(
        VisualContextGroup group,
        IReadOnlyList<WordImageRow> rows,
        CancellationToken ct = default)
    {
        if (group.RowIds.Count == 0)
            return;

        var orderedRows = group.RowIds
            .Select(rowId => rows.FirstOrDefault(row => row.RowId == rowId))
            .Where(row => row != null)
            .Cast<WordImageRow>()
            .OrderBy(row => row.OrderIndex)
            .ToList();

        if (orderedRows.Count == 0)
            return;

        var response = await TryRunAsync(group, orderedRows, ct);
        ApplyResponse(group, orderedRows, response);
    }

    private async Task<SentenceEditDeltaResponse?> TryRunAsync(
        VisualContextGroup group,
        IReadOnlyList<WordImageRow> rows,
        CancellationToken ct)
    {
        var systemPrompt = """
        You identify localized image-edit deltas for grouped English textbook sentence illustrations.

        The rows share context, but each row still needs its own image.
        Your output is NOT a drawing prompt. Return only structured JSON wrapped in <Output></Output>.

        Task:
        Use the FIRST row in the group as the fixed anchor source image for every editable target row.
        For each later target row, compare that target row directly with the first row and describe the single smallest changed local element as one operation.

        Rules:
        1. The first row is the only reference row. Return referenceRowId equal to the first row's rowId for every editable later row.
        2. Never chain edits through the second row, third row, or any generated edit result.
        3. If the target row cannot be obtained from the first row by a small localized semantic edit, return no operations and confidence <= 0.5.
        4. Return at most one operation per target row. If more than one element or action must change, return no operations and confidence <= 0.5.
        5. Make from/to the shortest possible visual fragments, ideally 1 to 4 words.
        6. Do not include people names, locations, scene labels, camera, layout, style, color palette, line art, lighting, background preservation, or full scene composition in from/to.
        7. Do not include English text, captions, speech bubbles, labels, or arrows as required edits unless the sentence explicitly teaches a visible mark.
        8. Prefer simple operations: replace, update, add, remove.
        9. Use minimal concrete visual fragments, such as "box" -> "apple", "pencil" -> "pen", "walking" -> "bus", "large room" -> "small room".
        10. Questions that only ask about a topic usually should not be used as edit targets unless there is a clear visual object/state.
        11. Do not return locator, rationale, scene, style, preservation, or negative-prompt fields.

        Return JSON:
        {
          "deltas": [
            {
              "rowId": 2,
              "referenceRowId": 1,
              "confidence": 0.92,
              "operations": [
                {
                  "operation": "replace | update | add | remove",
                  "target": "optional minimal edit area",
                  "from": "minimal source fragment",
                  "to": "minimal target fragment"
                }
              ]
            }
          ]
        }

        Output only <Output>{json}</Output>.
        """;

        var payload = new
        {
            group.GroupId,
            group.GroupType,
            group.SceneSetting,
            AnchorRow = new
            {
                rowId = rows[0].RowId,
                sentence = rows[0].English,
                rows[0].Chinese,
                rows[0].Speaker,
                group.FindMember(rows[0].RowId)?.VisualFocus,
                group.FindMember(rows[0].RowId)?.VisualAction,
                group.FindMember(rows[0].RowId)?.VariableElements
            },
            Characters = group.Characters,
            Members = rows.Select(row =>
            {
                var member = group.FindMember(row.RowId);
                return new
                {
                    rowId = row.RowId,
                    orderIndex = row.OrderIndex,
                    sentence = row.English,
                    row.Chinese,
                    row.Speaker,
                    member?.VisualFocus,
                    member?.VisualAction,
                    member?.VariableElements
                };
            })
        };

        try
        {
            return await planClient.SendAsJsonAsync<SentenceEditDeltaResponse>(
                systemPrompt,
                JsonSerializer.Serialize(payload, DefaultSerializeSetting.Default),
                "gpt-5.4-mini",
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sentence edit delta agent failed for group {GroupId}.", group.GroupId);
            return null;
        }
    }

    private static void ApplyResponse(
        VisualContextGroup group,
        IReadOnlyList<WordImageRow> rows,
        SentenceEditDeltaResponse? response)
    {
        var anchorReferenceRowId = rows[0].RowId;
        var rowIds = rows.Select(row => row.RowId).ToHashSet();
        var orderByRowId = rows.ToDictionary(row => row.RowId, row => row.OrderIndex);
        var deltas = response?.Deltas?
            .Where(delta => rowIds.Contains(delta.RowId))
            .GroupBy(delta => delta.RowId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First())
            ?? [];

        foreach (var row in rows)
        {
            var member = group.FindMember(row.RowId);
            if (member == null)
                continue;

            if (row.RowId == anchorReferenceRowId)
            {
                member.EditDelta = BuildNoEditDelta(row.RowId, anchorReferenceRowId);
                continue;
            }

            if (!deltas.TryGetValue(row.RowId, out var delta))
            {
                member.EditDelta = BuildNoEditDelta(row.RowId, anchorReferenceRowId);
                continue;
            }

            member.EditDelta = NormalizeDelta(delta, row.RowId, anchorReferenceRowId, orderByRowId);
        }
    }

    private static SentenceImageEditDelta NormalizeDelta(
        SentenceImageEditDelta delta,
        long rowId,
        long anchorReferenceRowId,
        Dictionary<long, int> orderByRowId)
    {
        var hasValidReference = delta.ReferenceRowId == anchorReferenceRowId &&
                                orderByRowId.TryGetValue(delta.ReferenceRowId, out var referenceOrder) &&
                                orderByRowId.TryGetValue(rowId, out var rowOrder) &&
                                referenceOrder < rowOrder;
        if (!hasValidReference)
            return BuildNoEditDelta(rowId, anchorReferenceRowId);

        var referenceRowId = delta.ReferenceRowId;

        var operations = delta.Operations
            .Where(operation => operation.HasConcreteChange())
            .Select(operation => new SentenceImageEditOperation
            {
                Operation = NormalizeOperation(operation.Operation),
                Target = Clean(operation.Target),
                From = Clean(operation.From),
                To = Clean(operation.To),
                RegionHint = null
            })
            .Where(operation => operation.HasConcreteChange())
            .ToList();
        if (operations.Count != 1)
            return BuildNoEditDelta(rowId, anchorReferenceRowId);

        return new SentenceImageEditDelta
        {
            RowId = rowId,
            ReferenceRowId = referenceRowId,
            SourceLocator = null,
            Operations = operations,
            Confidence = Math.Clamp(delta.Confidence, 0, 1),
            Rationale = null
        };
    }

    private static SentenceImageEditDelta BuildNoEditDelta(long rowId, long referenceRowId)
    {
        return new SentenceImageEditDelta
        {
            RowId = rowId,
            ReferenceRowId = referenceRowId,
            Confidence = 0
        };
    }

    private static string NormalizeOperation(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "replace" => "replace",
            "update" => "update",
            "change" => "update",
            "add" => "add",
            "remove" => "remove",
            _ => "replace"
        };
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal sealed class SentenceEditDeltaResponse
    {
        [JsonPropertyName("deltas")]
        public List<SentenceImageEditDelta>? Deltas { get; set; }
    }
}
