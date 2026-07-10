using MS.Microservice.AI.Core.Images.Models;
using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Builds image-edit prompts from structured edit deltas.
/// </summary>
public static class SentenceImageEditPromptBuilder
{
    public static bool CanUseReferenceEdit(SentenceImageEditDelta? delta)
    {
        return delta is { Confidence: >= 0.6 } && GetConcreteOperations(delta.Operations).Count == 1;
    }

    public static string BuildPrompt(SentenceImageEditDelta delta)
    {
        var operations = GetConcreteOperations(delta.Operations);
        return operations.Count == 1 ? BuildOperationText(operations[0]) : string.Empty;
    }

    private static List<SentenceImageEditOperation> GetConcreteOperations(IEnumerable<SentenceImageEditOperation> operations)
    {
        return operations
            .Where(operation => operation.HasConcreteChange())
            .ToList();
    }

    private static string BuildOperationText(SentenceImageEditOperation operation)
    {
        var op = operation.Operation?.Trim().ToLowerInvariant() ?? string.Empty;
        var target = MinimizeFragment(operation.Target);
        var from = MinimizeFragment(operation.From);
        var to = MinimizeFragment(operation.To);

        return op switch
        {
            "replace" when !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to) => $"Only edit: {from} -> {to}.",
            "update" or "change" when !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(to) => $"Only edit: {target} -> {to}.",
            "add" when !string.IsNullOrWhiteSpace(to) => $"Only add: {to}.",
            "add" when !string.IsNullOrWhiteSpace(target) => $"Only add: {target}.",
            "remove" when !string.IsNullOrWhiteSpace(from) => $"Only remove: {from}.",
            "remove" when !string.IsNullOrWhiteSpace(target) => $"Only remove: {target}.",
            _ when !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to) => $"Only edit: {from} -> {to}.",
            _ when !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(to) => $"Only edit: {target} -> {to}.",
            _ => string.Empty
        };
    }

    private static string MinimizeFragment(string? value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        var transport = NormalizeTransportFragment(cleaned);
        return RemoveArticles(transport);
    }

    private static string NormalizeTransportFragment(string value)
    {
        var cleaned = RemoveArticles(value);
        if (cleaned.Equals("walking on foot", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("on foot", StringComparison.OrdinalIgnoreCase))
            return "walking";

        var ridingMatch = Regex.Match(
            cleaned,
            @"^(riding|taking|driving|using)\s+(in|on)?\s*(bus|car|bike|bicycle|taxi|train|subway|metro|plane|ship|boat)$",
            RegexOptions.IgnoreCase);
        if (ridingMatch.Success)
            return ridingMatch.Groups[3].Value.ToLowerInvariant();

        var byMatch = Regex.Match(
            cleaned,
            @"^(going|coming|traveling|travelling)\s+by\s+(bus|car|bike|bicycle|taxi|train|subway|metro|plane|ship|boat)$",
            RegexOptions.IgnoreCase);
        if (byMatch.Success)
            return byMatch.Groups[2].Value.ToLowerInvariant();

        var directByMatch = Regex.Match(
            cleaned,
            @"^by\s+(bus|car|bike|bicycle|taxi|train|subway|metro|plane|ship|boat)$",
            RegexOptions.IgnoreCase);
        if (directByMatch.Success)
            return directByMatch.Groups[1].Value.ToLowerInvariant();

        return cleaned;
    }

    private static string RemoveArticles(string value)
    {
        return Regex.Replace(value, @"\b(a|an|the)\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Trim().TrimEnd('.', ';', ':');
    }
}
