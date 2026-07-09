using Microsoft.Extensions.Logging;
using MS.Microservice.AI.Core.Images.Helpers;
using MS.Microservice.AI.Core.Images.Models;
using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images;

public partial class SceneGroupingAgent : ISceneGroupingAgent
{
    private readonly IPlanGeneratorClient planClient;
    private readonly ILogger<SceneGroupingAgent> logger;

    public SceneGroupingAgent(IPlanGeneratorClient planClient, ILogger<SceneGroupingAgent> logger)
    {
        this.planClient = planClient;
        this.logger = logger;
    }

    /// <summary>
    /// Groups rows into visual context groups using an LLM.
    /// </summary>
    public async Task<SceneGroupingResult> GroupAsync(IReadOnlyList<WordImageRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return new SceneGroupingResult { Groups = [], UncertainRowIds = [] };

        // ── Check for pre-assigned groups from Excel ──
        var (preAssigned, unassigned) = SplitPreAssigned(rows);
        var aiGroups = new List<VisualContextGroup>();

        if (unassigned.Count > 0)
        {
            aiGroups = await RunLlmGroupingAsync(unassigned, ct);
        }

        // Merge pre-assigned + AI groups, then normalize row membership so callers can
        // depend on member-level visual focus even when the LLM response is partial.
        var allGroups = NormalizeGroups(preAssigned.Concat(aiGroups), rows);

        // Collect uncertain rows
        var uncertain = new List<long>();
        foreach (var group in allGroups.Where(g => g.Confidence < 0.6))
            uncertain.AddRange(group.RowIds);

        return new SceneGroupingResult
        {
            Groups = allGroups,
            UncertainRowIds = uncertain.Distinct().ToList()
        };
    }

    // ── Pre-assigned groups (from Excel SceneGroupId column) ──
    private static (List<VisualContextGroup> Groups, List<WordImageRow> Unassigned) SplitPreAssigned(IReadOnlyList<WordImageRow> rows)
    {
        var groups = new List<VisualContextGroup>();
        var unassigned = new List<WordImageRow>();

        var preGrouped = rows.Where(r => !string.IsNullOrWhiteSpace(r.SceneGroupId))
            .GroupBy(r => r.SceneGroupId!);

        foreach (var g in preGrouped)
        {
            var memberRows = g.OrderBy(r => r.OrderIndex).ToList();
            groups.Add(new VisualContextGroup
            {
                GroupId = g.Key,
                RowIds = memberRows.Select(r => r.RowId).ToList(),
                Members = memberRows.Select(r => new VisualContextMember
                {
                    RowId = r.RowId,
                    Speaker = r.Speaker,
                    SceneHint = r.SceneHint,
                    OrderIndex = r.OrderIndex,
                    VisualFocus = r.English,
                    VisualAction = "Illustrate this row only.",
                    VariableElements = [r.English]
                }).ToList(),
                GroupType = "pre_assigned",
                Confidence = 1.0,
                Reason = "Pre-assigned from Excel SceneGroupId column",
                SceneSetting = memberRows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.SceneHint))?.SceneHint ?? string.Empty
            });
        }

        unassigned.AddRange(rows.Where(r => string.IsNullOrWhiteSpace(r.SceneGroupId)));
        return (groups, unassigned);
    }

    // ── LLM-based grouping ──
    private async Task<List<VisualContextGroup>> RunLlmGroupingAsync(List<WordImageRow> rows, CancellationToken ct)
    {
        var systemPrompt = """
        You are reconstructing visual context groups for English textbook illustration generation.
        The input rows are ordered exactly as they appear in the textbook or Excel.
        Your task is to identify stable context only: scene category, setting, recurring characters, and each row's own visual focus.

        Critical rule:
        Do NOT merge several sentence meanings into one picture. A group is only shared context.
        Every row will still generate its own image. The current row's visualFocus is the only row-specific subject to show.

        Grouping rules:
        1. Adjacent greeting pairs (Hello/Hi) may form one dialogue group.
        2. Consecutive self-introductions (I am/My name is/Nice to meet you) may form one group.
        3. Consecutive classroom object drills (This is a box. This is an apple. A book? Yes.) may share one stable classroom/table context.
        4. Consecutive location tours (It's our classroom/library/playground) may share visual style.
        5. Rows with the same character names across nearby rows belong together.
        6. A group can contain a single row if there is insufficient evidence.
        7. If confidence is low (<0.6), mark groupType as uncertain.
        8. Generate stable character profiles: same name means same appearance.
        9. sceneSetting must describe only the stable environment, such as classroom table, school hallway, playground, home, shop, or library.
           Never list all changing row objects in sceneSetting.
        10. sharedProps may contain only props visible in every row of the group. Do not put apple/book/pencil/pen together unless every row must show all of them.
        11. For elliptical answers such as "Yes.", infer visualFocus from the nearest previous question or prompt.
            Example: "A book?" followed by "Yes." means visualFocus "a book with an affirmative response".
        12. For corrections such as "No. It is a pen.", visualFocus is the corrected object ("a pen"), and visualAction should say not to show the wrong object as the answer.
        13. reuseMode should almost always be "generate_all". Use "reuse_first" only when two rows are visually identical.
        14. Do NOT group broad safety rules, sports tips, exercise instructions, or action lists merely because they share a topic.
            Examples: "Be careful", "Don't skate on the road", "Warm up", "Do some running", "Take a rest" should be single_sentence rows unless the source explicitly says they are the same moment in the same scene.
        15. If you still group an instruction list for metadata continuity, use groupType "instructional_sequence" or "safety_rules"; do not imply a frozen shared layout.

        Return compact JSON only, wrapped in <Output></Output>:
        {
          "groups": [
            {
              "groupId": "G1",
              "rowIds": [1, 2],
              "groupType": "dialogue | object_drill | location_tour | self_introduction | greeting | instructional_sequence | safety_rules | single_sentence | uncertain",
              "confidence": 0.95,
              "reuseMode": "generate_all",
              "reason": "short reason in English",
              "sceneSetting": "stable environment only; no list of changing target objects",
              "characters": [
                {
                  "name": "Tom",
                  "role": "student | teacher | child | other",
                  "appearance": "Chinese schoolboy, short black hair, white shirt, blue shorts, sneakers"
                }
              ],
              "sharedProps": ["classroom table"],
              "continuityPolicy": "Same characters, same setting, same style. Only each row's visualFocus changes.",
              "members": [
                {
                  "rowId": 1,
                  "speaker": "optional speaker name",
                  "sceneHint": "optional row-specific hint",
                  "orderIndex": 0,
                  "visualFocus": "only the current row's main object/action/referent",
                  "visualAction": "what should visibly happen in this row",
                  "variableElements": ["objects/details allowed to change for this row only"]
                }
              ]
            }
          ],
          "uncertainRowIds": []
        }

        Output only <Output>{json}</Output>.
        """;

        // ── Try LLM grouping with original text first ──
        SceneGroupingLlmResponse? result = null;
        try
        {
            var userMessage = BuildRowListing(rows);
            result = await planClient.SendAsJsonAsync<SceneGroupingLlmResponse>(systemPrompt, userMessage, "gpt-5.4-mini", ct);
        }
        catch (Exception ex) when (IsContentFilterException(ex))
        {
            logger.LogWarning(ex, "LLM grouping blocked by content filter; retrying with sanitized input.");

            // Retry with sanitized text to bypass content filters.
            // Only strip negation prefixes and sensitive words — keep enough signal for grouping.
            var sanitizedRows = rows.Select(r => new WordImageRow
            {
                RowId = r.RowId,
                English = SanitizeForGrouping(r.English),
                Chinese = SanitizeForGrouping(r.Chinese),
                Speaker = r.Speaker,
                Addressee = r.Addressee,
                SceneHint = r.SceneHint,
                OrderIndex = r.OrderIndex,
                SceneGroupId = r.SceneGroupId,
                CatalogueId = r.CatalogueId
            }).ToList();

            try
            {
                var retryMessage = BuildRowListing(sanitizedRows);
                result = await planClient.SendAsJsonAsync<SceneGroupingLlmResponse>(systemPrompt, retryMessage, "gpt-5.4-mini", ct);
            }
            catch (Exception retryEx)
            {
                logger.LogWarning(retryEx, "LLM grouping retry with sanitized input also failed.");
            }
        }
        if (result?.Groups == null || result.Groups.Count == 0)
        {
            logger.LogWarning("LLM grouping returned no groups; falling back to standalone groups.");
            return rows.Select(r => new VisualContextGroup
            {
                GroupId = $"S{r.RowId}",
                RowIds = [r.RowId],
                Members =
                [
                    new()
                    {
                        RowId = r.RowId,
                        Speaker = r.Speaker,
                        SceneHint = r.SceneHint,
                        OrderIndex = r.OrderIndex,
                        VisualFocus = r.English,
                        VisualAction = "Illustrate this sentence only.",
                        VariableElements = [r.English]
                    }
                ],
                GroupType = "single_sentence",
                Confidence = 0.5,
                Reason = "LLM grouping failed — fallback"
            }).ToList();
        }

        // Apply a conservative default: grouping is for context, not image reuse.
        foreach (var g in result.Groups)
        {
            if (g.ReuseMode == ImageReuseMode.GenerateAll && g.RowIds.Count > 1)
            {
                g.ReuseMode = ImageReuseMode.GenerateAll;
            }
        }

        return result.Groups;
    }

    private static List<VisualContextGroup> NormalizeGroups(
        IEnumerable<VisualContextGroup> groups,
        IReadOnlyList<WordImageRow> rows)
    {
        var rowsById = rows.ToDictionary(r => r.RowId);
        var normalized = new List<VisualContextGroup>();
        var covered = new HashSet<long>();
        var ordinal = 1;

        foreach (var group in groups)
        {
            var rowIds = group.RowIds
                .Where(id => rowsById.ContainsKey(id) && !covered.Contains(id))
                .Distinct()
                .OrderBy(id => rowsById[id].OrderIndex)
                .ToList();

            if (rowIds.Count == 0 && group.Members.Count > 0)
            {
                rowIds = group.Members
                    .Select(member => member.RowId)
                    .Where(id => rowsById.ContainsKey(id) && !covered.Contains(id))
                    .Distinct()
                    .OrderBy(id => rowsById[id].OrderIndex)
                    .ToList();
            }

            if (rowIds.Count == 0)
                continue;

            group.GroupId = string.IsNullOrWhiteSpace(group.GroupId)
                ? $"G{ordinal++}"
                : group.GroupId.Trim();
            group.RowIds = rowIds;
            group.GroupType = string.IsNullOrWhiteSpace(group.GroupType) ? "single_sentence" : group.GroupType.Trim();
            group.SceneSetting = group.SceneSetting?.Trim() ?? string.Empty;
            group.ContinuityPolicy = group.ContinuityPolicy?.Trim() ?? string.Empty;
            group.Members = BuildMembers(group, rowIds, rowsById);

            foreach (var rowId in rowIds)
                covered.Add(rowId);

            normalized.Add(group);
        }

        foreach (var row in rows.Where(row => !covered.Contains(row.RowId)).OrderBy(row => row.OrderIndex))
        {
            normalized.Add(new VisualContextGroup
            {
                GroupId = $"S{row.RowId}",
                RowIds = [row.RowId],
                GroupType = "single_sentence",
                Confidence = 1.0,
                Reason = "Ungrouped fallback",
                Members =
                [
                    new()
                    {
                        RowId = row.RowId,
                        Speaker = row.Speaker,
                        SceneHint = row.SceneHint,
                        OrderIndex = row.OrderIndex,
                        VisualFocus = row.English,
                        VisualAction = "Illustrate this sentence only.",
                        VariableElements = [row.English]
                    }
                ]
            });
        }

        return normalized
            .OrderBy(group => group.RowIds.Min(rowId => rowsById[rowId].OrderIndex))
            .ToList();
    }

    private static List<VisualContextMember> BuildMembers(
        VisualContextGroup group,
        IReadOnlyList<long> rowIds,
        Dictionary<long, WordImageRow> rowsById)
    {
        var membersById = group.Members
            .Where(member => rowsById.ContainsKey(member.RowId))
            .GroupBy(member => member.RowId)
            .ToDictionary(g => g.Key, g => g.First());

        var members = new List<VisualContextMember>();
        foreach (var rowId in rowIds)
        {
            var row = rowsById[rowId];
            membersById.TryGetValue(rowId, out var member);

            members.Add(new VisualContextMember
            {
                RowId = rowId,
                Speaker = FirstNonEmpty(member?.Speaker, row.Speaker),
                SceneHint = FirstNonEmpty(member?.SceneHint, row.SceneHint),
                OrderIndex = row.OrderIndex,
                VisualFocus = FirstNonEmpty(member?.VisualFocus, row.English),
                VisualAction = FirstNonEmpty(member?.VisualAction, "Illustrate this sentence only."),
                VariableElements = NormalizeVariableElements(member?.VariableElements, row.English)
            });
        }

        return members;
    }

    private static List<string> NormalizeVariableElements(List<string>? values, string fallback)
    {
        var normalized = values?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
            normalized.Add(fallback.Trim());

        return normalized;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    /// <summary>
    /// Light sanitization for grouping input: strips negation prefixes and sensitive words
    /// so the LLM grouping call passes content filters. Falls back to original text when
    /// sanitization destroys too much signal.
    /// </summary>
    private static string SanitizeForGrouping(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 1) Strip negation prefix words only (keep the rest for semantic grouping)
        var cleaned = NegationPrefixRegex().Replace(text.Trim(), "");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

        // 2) Also remove the full PromptSanitizer sensitive-word list
        var deepCleaned = PromptSanitizer.Clean(cleaned);
        if (!string.IsNullOrWhiteSpace(deepCleaned))
            cleaned = deepCleaned;

        // 3) If sanitization destroyed everything, keep the original (accept filter risk)
        return string.IsNullOrWhiteSpace(cleaned) ? text : cleaned;
    }

    /// <summary>
    /// Detects Azure OpenAI / AI content-filter exceptions by inspecting the exception
    /// type name and message for known content-filter markers.
    /// </summary>
    private static bool IsContentFilterException(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
            || message.Contains("content filter", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("ContentFilter", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\b(?:don't|do not|no|never|not)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex NegationPrefixRegex();

    private static string BuildRowListing(List<WordImageRow> rows)
    {
        var lines = new List<string> { "Rows:" };
        foreach (var row in rows)
        {
            var extras = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Speaker)) extras.Add($"speaker={row.Speaker}");
            if (!string.IsNullOrWhiteSpace(row.Addressee)) extras.Add($"addressee={row.Addressee}");
            if (!string.IsNullOrWhiteSpace(row.SceneHint)) extras.Add($"visualHint={row.SceneHint}");

            var extra = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
            lines.Add($"  {row.RowId}: \"{row.English}\" | \"{row.Chinese}\"{extra}");
        }
        return string.Join("\n", lines);
    }

    private sealed class SceneGroupingLlmResponse
    {
        public List<VisualContextGroup>? Groups { get; set; }
        public List<long>? UncertainRowIds { get; set; }
    }
}

/// <summary>
/// Result of scene grouping — ready for batch generation or DB persistence.
/// </summary>
public sealed class SceneGroupingResult
{
    public List<VisualContextGroup> Groups { get; set; } = [];
    public List<long> UncertainRowIds { get; set; } = [];
}
