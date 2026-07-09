namespace MS.Microservice.AI.Qwen;

/// <summary>
/// Qwen-specific helpers for reference-image editing that are NOT part of the
/// generic OpenAI-compatible provider base classes.
/// </summary>
internal static class QwenImageEditHelper
{
    /// <summary>
    /// Idempotent helper that wraps an edit prompt with SOURCE IMAGE protection
    /// words. If the prompt already begins with the protection prefix it is
    /// returned unchanged; otherwise the prefix is prepended.
    /// </summary>
    internal static string WrapEditPromptWithSourceProtection(string prompt)
    {
        const string prefix = "Use the SOURCE IMAGE as the base canvas.";
        if (prompt.StartsWith(prefix, StringComparison.Ordinal))
            return prompt;

        return string.Join(" ",
        [
            prefix,
            "The attached edit instruction is the complete change list.",
            "All elements outside the named target area remain pixel-faithful to the SOURCE IMAGE.",
            prompt
        ]);
    }

    /// <summary>
    /// Normalizes size strings for Qwen multimodal API:
    /// <c>1024x1024</c> / <c>1024X1024</c> → <c>1024*1024</c>.
    /// Already-normalized formats (<c>1K</c>, <c>2K</c>, <c>4:3</c>, <c>16:9</c>, <c>1024*1024</c>) are left unchanged.
    /// </summary>
    internal static string NormalizeSize(string size)
    {
        // Already in Qwen format, keep as-is
        if (size.Contains('*') || size is "1K" or "2K" or "4:3" or "16:9")
            return size;

        return size.Replace('x', '*').Replace('X', '*');
    }
}
