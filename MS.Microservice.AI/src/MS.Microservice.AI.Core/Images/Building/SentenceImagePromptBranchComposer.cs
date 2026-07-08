using System.Text.RegularExpressions;

namespace MS.Microservice.AI.Core.Images.Building;

/// <summary>
/// Appends sentence-scenario-specific prompt branches before the standard image prompt pipeline runs.
/// New teaching scenarios can be added as additional branch rules without changing the generation flow.
/// </summary>
public static partial class SentenceImagePromptBranchComposer
{
    private static readonly ISentenceImagePromptBranchRule[] Rules =
    [
        new SparseScenePromptBranchRule(),
        new SpecificObjectFocusPromptBranchRule()
    ];

    public static string Apply(string imageInput, SentenceImagePromptBranchContext context)
    {
        if (string.IsNullOrWhiteSpace(imageInput))
            return imageInput;

        var branches = Rules
            .Select(rule => rule.TryBuild(context))
            .Where(branch => branch != null && !string.IsNullOrWhiteSpace(branch.Prompt))
            .Cast<SentenceImagePromptBranch>()
            .ToList();

        if (branches.Count == 0)
            return imageInput;

        var branchPrompt = string.Join(" ", branches.Select(branch => $"Prompt branch {branch.Name}: {branch.Prompt}"));
        return MergeHint(imageInput, branchPrompt);
    }

    private static string MergeHint(string imageInput, string prompt)
    {
        if (imageInput.Contains('(') && imageInput.EndsWith(')'))
        {
            var lastParen = imageInput.LastIndexOf('(');
            var text = imageInput[..lastParen].TrimEnd();
            var hint = imageInput[(lastParen + 1)..^1].TrimEnd();
            return $"{text}({hint}. {prompt}.)";
        }

        return $"{imageInput}({prompt}.)";
    }

    private sealed class SparseScenePromptBranchRule : ISentenceImagePromptBranchRule
    {
        public SentenceImagePromptBranch? TryBuild(SentenceImagePromptBranchContext context)
        {
            if (string.IsNullOrWhiteSpace(context.SentenceText))
                return null;

            var prompt =
                "Sparse-scene composition. Use one primary action and one primary environmental anchor. " +
                "Use only props that are necessary for the current sentence. " +
                "For playground scenes, show one main facility only. Display surfaces such as classroom boards are absent unless explicitly named by the sentence.";

            return new SentenceImagePromptBranch("sparse-scene", prompt);
        }
    }

    private sealed partial class SpecificObjectFocusPromptBranchRule : ISentenceImagePromptBranchRule
    {
        public SentenceImagePromptBranch? TryBuild(SentenceImagePromptBranchContext context)
        {
            var targetObject = TryExtractTargetObject(context.SentenceText);
            if (targetObject == null &&
                IsEllipticalObjectAnswer(context.SentenceText) &&
                IsObjectDrill(context.GroupType))
            {
                targetObject = TryExtractTargetObjectFromVisualFocus(context.VisualFocus);
            }

            if (string.IsNullOrWhiteSpace(targetObject))
                return null;

            var prompt =
                $"Target-object focus. Depict {targetObject} as the clear central focus for this sentence. " +
                $"Use a red arrow pointer aimed at {targetObject} when the scene contains multiple objects. " +
                "Keep any surrounding objects minimal, small, and secondary.";

            return new SentenceImagePromptBranch("target-object-focus", prompt);
        }

        private static bool IsObjectDrill(string? groupType)
        {
            return string.Equals(groupType?.Trim(), "object_drill", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEllipticalObjectAnswer(string? sentenceText)
        {
            if (string.IsNullOrWhiteSpace(sentenceText))
                return false;

            return EllipticalAnswerRegex().IsMatch(NormalizeSentence(sentenceText));
        }

        private static string? TryExtractTargetObject(string? sentenceText)
        {
            if (string.IsNullOrWhiteSpace(sentenceText))
                return null;

            var text = NormalizeSentence(sentenceText);
            return ExtractObject(DeclarativeObjectRegex().Match(text))
                ?? ExtractObject(QuestionObjectRegex().Match(text))
                ?? ExtractObject(EllipticalObjectQuestionRegex().Match(text));
        }

        private static string? TryExtractTargetObjectFromVisualFocus(string? visualFocus)
        {
            if (string.IsNullOrWhiteSpace(visualFocus))
                return null;

            var text = NormalizeSentence(visualFocus);
            return ExtractObject(ArticleObjectRegex().Match(text));
        }

        private static string NormalizeSentence(string value)
        {
            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static string? ExtractObject(Match match)
        {
            if (!match.Success)
                return null;

            var article = match.Groups["article"].Value.Trim().ToLowerInvariant();
            var noun = match.Groups["object"].Value.Trim();
            if (string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(noun))
                return null;

            noun = TrimObjectTail(noun);
            return string.IsNullOrWhiteSpace(noun) ? null : $"{article} {noun}";
        }

        private static string TrimObjectTail(string value)
        {
            var trimmed = Regex.Replace(value, @"\s+", " ").Trim();
            trimmed = Regex.Replace(trimmed, @"\s+(with|being|as|on|in|near|beside|and|while)\b.*$", "", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"[.!?。！？]+$", "");
            return trimmed.Trim();
        }

        [GeneratedRegex(@"\b(?:this|that|it)\s+is\s+(?<article>a|an|the)\s+(?<object>[A-Za-z][A-Za-z'\-]*(?:\s+[A-Za-z][A-Za-z'\-]*){0,3})(?:[.!?])?$", RegexOptions.IgnoreCase)]
        private static partial Regex DeclarativeObjectRegex();

        [GeneratedRegex(@"^\s*is\s+(?:it|this|that)\s+(?<article>a|an|the)\s+(?<object>[A-Za-z][A-Za-z'\-]*(?:\s+[A-Za-z][A-Za-z'\-]*){0,3})(?:[?])?$", RegexOptions.IgnoreCase)]
        private static partial Regex QuestionObjectRegex();

        [GeneratedRegex(@"^\s*(?<article>a|an|the)\s+(?<object>[A-Za-z][A-Za-z'\-]*(?:\s+[A-Za-z][A-Za-z'\-]*){0,3})\s*\?$", RegexOptions.IgnoreCase)]
        private static partial Regex EllipticalObjectQuestionRegex();

        [GeneratedRegex(@"\b(?<article>a|an|the)\s+(?<object>[A-Za-z][A-Za-z'\-]*(?:\s+[A-Za-z][A-Za-z'\-]*){0,3})(?:\b|[.!?])", RegexOptions.IgnoreCase)]
        private static partial Regex ArticleObjectRegex();

        [GeneratedRegex(@"^(yes|no)\.?$", RegexOptions.IgnoreCase)]
        private static partial Regex EllipticalAnswerRegex();
    }
}

public sealed record SentenceImagePromptBranchContext(
    string SentenceText,
    string? VisualFocus = null,
    string? GroupType = null);

public sealed record SentenceImagePromptBranch(string Name, string Prompt);

public interface ISentenceImagePromptBranchRule
{
    SentenceImagePromptBranch? TryBuild(SentenceImagePromptBranchContext context);
}
