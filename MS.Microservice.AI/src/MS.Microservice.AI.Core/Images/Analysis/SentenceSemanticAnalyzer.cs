using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images.Analysis;

/// <summary>
/// Deterministic sentence-level semantic analysis.
/// Used to identify prohibition, safety warnings, classroom mentions, and action keywords
/// without requiring an LLM call.
/// </summary>
public static partial class SentenceSemanticAnalyzer
{
    public static bool IsProhibitive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ProhibitiveRegex().IsMatch(text);
    }

    public static bool IsCareful(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return CarefulRegex().IsMatch(text);
    }

    public static bool MentionsClassroom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ClassroomRegex().IsMatch(text);
    }

    public static bool MentionsRunning(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return RunningRegex().IsMatch(text);
    }

    /// <summary>
    /// Returns true when 'a' and 'b' share significant word overlap (>50%).
    /// </summary>
    public static bool HasSignificantOverlap(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        var wordsA = ToWordSet(a);
        var wordsB = ToWordSet(b);
        var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        return intersection >= Math.Min(wordsA.Count, wordsB.Count) / 2;
    }

    private static HashSet<string> ToWordSet(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b(don't|do not|never|no)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProhibitiveRegex();

    [GeneratedRegex(@"\b(be careful|watch out|look out)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CarefulRegex();

    [GeneratedRegex(@"\b(classroom|class|school)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassroomRegex();

    [GeneratedRegex(@"\b(run|runs|running)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RunningRegex();
}
