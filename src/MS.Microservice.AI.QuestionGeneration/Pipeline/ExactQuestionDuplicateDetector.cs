using System.Security.Cryptography;
using System.Text;
using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Pipeline;

public sealed class ExactQuestionDuplicateDetector : IQuestionDuplicateDetector
{
    public QuestionDuplicateMatch? FindDuplicate(
        QuestionCandidate candidate,
        IQuestionDefinition definition,
        QuestionContextSnapshot context,
        IReadOnlyCollection<QuestionReference> acceptedInBatch)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(acceptedInBatch);

        var comparable = Normalize(definition.BuildComparableText(candidate));
        var fingerprint = Fingerprint(comparable);
        var existing = context.ExistingQuestions
            .Concat(acceptedInBatch)
            .FirstOrDefault(reference =>
                string.Equals(reference.Fingerprint, fingerprint, StringComparison.Ordinal) ||
                string.Equals(Normalize(reference.ComparableText), comparable, StringComparison.Ordinal));
        return existing is null
            ? null
            : new(existing.Fingerprint, "An exact normalized question already exists.");
    }

    public static QuestionReference CreateReference(
        QuestionCandidate candidate,
        IQuestionDefinition definition)
    {
        var comparable = Normalize(definition.BuildComparableText(candidate));
        return new(Fingerprint(comparable), comparable);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
