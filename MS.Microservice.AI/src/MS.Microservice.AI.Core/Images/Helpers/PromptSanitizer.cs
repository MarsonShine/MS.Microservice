using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images.Helpers;

/// <summary>
/// Strips negative and sensitive language from text, leaving only positive visual descriptions.
/// Designed to prevent Qwen/DashScope content-filter rejection triggered by sensitive keywords
/// even when they appear in negation form (e.g., "no violence" still triggers "violence").
/// </summary>
public static partial class PromptSanitizer
{
    /// <summary>
    /// Strips negative and sensitive language, returning a clean positive-only phrase or null.
    /// </summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = StripNegationPhrases(text);
        cleaned = RemoveSensitiveWords(cleaned);
        cleaned = CleanupArtifacts(cleaned);

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string StripNegationPhrases(string text)
    {
        var cleaned = text;
        cleaned = NegationRegex().Replace(cleaned, "");
        cleaned = NeverRegex().Replace(cleaned, "");
        cleaned = WithoutRegex().Replace(cleaned, "");
        cleaned = DontRegex().Replace(cleaned, "");
        cleaned = DoNotRegex().Replace(cleaned, "");
        cleaned = NotRegex().Replace(cleaned, "");
        return cleaned;
    }

    private static string RemoveSensitiveWords(string text)
    {
        var cleaned = text;
        foreach (var word in SensitiveWords)
        {
            var pattern = @"\b" + Regex.Escape(word) + @"\b";
            cleaned = Regex.Replace(cleaned, pattern, "", RegexOptions.IgnoreCase);
        }
        return cleaned;
    }

    private static string CleanupArtifacts(string text)
    {
        var cleaned = Regex.Replace(text, @"\s{2,}", " ");
        cleaned = Regex.Replace(cleaned, @"^[,;.\s]+", "");
        cleaned = Regex.Replace(cleaned, @"[,;.\s]+$", "");
        cleaned = Regex.Replace(cleaned, @"\s*,\s*,\s*", ", ");
        cleaned = Regex.Replace(cleaned, @"^\s*and\s+", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+and\s*$", "", RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private static readonly string[] SensitiveWords =
    [
        "strictly", "avoid", "forbidden", "prohibited", "must not", "should not",
        "barefoot", "bare feet", "naked",
        "violence", "violent", "sexual", "hateful", "disturbing", "adult content",
        "blood", "injury", "injured", "wound", "hurt",
        "accident", "falling", "crash", "danger", "dangerous", "frightening",
        "weapon", "military", "gun", "knife",
        "death", "dead", "dying", "kill",
        "crying in pain", "scream", "terror", "horror",
        "cropped", "cut-off", "cut off", "decapitated", "missing limbs",
        "partial", "floating torso", "limbless",
        "political", "religious symbol", "flag", "national emblem"
    ];

    [GeneratedRegex(@"\bno\s+\w+(\s+\w+){0,3}", RegexOptions.IgnoreCase)]
    private static partial Regex NegationRegex();

    [GeneratedRegex(@"\bnever\s+\w+(\s+\w+){0,3}", RegexOptions.IgnoreCase)]
    private static partial Regex NeverRegex();

    [GeneratedRegex(@"\bwithout\s+\w+(\s+\w+){0,3}", RegexOptions.IgnoreCase)]
    private static partial Regex WithoutRegex();

    [GeneratedRegex(@"\bdon't\s+\w+(\s+\w+){0,3}", RegexOptions.IgnoreCase)]
    private static partial Regex DontRegex();

    [GeneratedRegex(@"\bdo not\s+\w+(\s+\w+){0,3}", RegexOptions.IgnoreCase)]
    private static partial Regex DoNotRegex();

    [GeneratedRegex(@"\bnot\s+\w+(\s+\w+){0,2}", RegexOptions.IgnoreCase)]
    private static partial Regex NotRegex();
}
