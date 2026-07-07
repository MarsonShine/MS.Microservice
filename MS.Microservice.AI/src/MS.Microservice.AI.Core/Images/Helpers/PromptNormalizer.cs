namespace MS.Microservice.AI.Core.Images.Helpers;

public static class PromptNormalizer
{
    public static string NormalizeValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().TrimEnd('.');
        }
        return string.Empty;
    }

    public static string NormalizeSceneSetting(string? sceneSetting, string contentType)
    {
        var normalized = NormalizeValue(sceneSetting);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        // Alphabet cards use plain backgrounds for letter clarity
        if (contentType == Models.WordImageCardType.Alphabet)
            return string.Empty;

        // Only filter truly problematic scene descriptions
        var lower = normalized.ToLowerInvariant();
        string[] blocked = ["messy", "cluttered", "chaotic", "dark room", "crowded"];

        return blocked.Any(k => lower.Contains(k, StringComparison.Ordinal))
            ? string.Empty
            : normalized;
    }

    public static string NormalizeOverlayText(string? overlayText, string fallback)
    {
        return !string.IsNullOrWhiteSpace(overlayText) ? overlayText.Trim() : fallback.Trim();
    }

    public static void AddDistinct(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!list.Any(item => string.Equals(item.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)))
            list.Add(value.Trim());
    }

    public static void NormalizeList(List<string> list, int maxCount)
    {
        var normalized = list
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().TrimEnd('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
        list.Clear();
        list.AddRange(normalized);
    }

    public static bool ContainsAny(IEnumerable<string>? values, params string[] keywords)
    {
        if (values == null) return false;
        return values.Any(value =>
            keywords.Any(keyword =>
                value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }
}
