using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.AI.QuestionGeneration.Contracts;

[JsonConverter(typeof(QuestionTypeIdJsonConverter))]
public readonly record struct QuestionTypeId
{
    public QuestionTypeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed class QuestionTypeIdJsonConverter : JsonConverter<QuestionTypeId>
{
    public override QuestionTypeId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Question type identifiers must be JSON strings.");
        }

        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new JsonException("Question type identifiers cannot be empty.")
            : new QuestionTypeId(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        QuestionTypeId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed record QuestionReference(string Fingerprint, string ComparableText);

public sealed record QuestionEvidenceReference(string SourceId, string? Quote = null);

public sealed record QuestionContextSnapshot
{
    public required string ContextId { get; init; }

    public required string Version { get; init; }

    public required string Hash { get; init; }

    public required JsonElement Data { get; init; }

    public IReadOnlyList<QuestionReference> ExistingQuestions { get; init; } = [];
}

public sealed record QuestionBlueprint
{
    public required string BlueprintId { get; init; }

    public required QuestionTypeId QuestionType { get; init; }

    public required int Sequence { get; init; }

    public required string ContextVersion { get; init; }

    public required string ContextHash { get; init; }

    public required string SpecificationVersion { get; init; }

    public required JsonElement Constraints { get; init; }

    public IReadOnlyList<QuestionEvidenceReference> Evidence { get; init; } = [];
}

public abstract record QuestionCandidate
{
    public required string BlueprintId { get; init; }

    public required QuestionTypeId QuestionType { get; init; }
}

public sealed record QuestionPipelineManifest
{
    public required string PipelineVersion { get; init; }

    public required string HarnessVersion { get; init; }

    public required string PromptVersion { get; init; }

    public required string SchemaVersion { get; init; }

    public required string RuleSetVersion { get; init; }

    public required string RubricVersion { get; init; }

    public static QuestionPipelineManifest Default { get; } = new()
    {
        PipelineVersion = "question-generation-v1",
        HarnessVersion = "bounded-review-repair-v1",
        PromptVersion = "host-defined-v1",
        SchemaVersion = "runtime-json-schema-v1",
        RuleSetVersion = "host-defined-v1",
        RubricVersion = "host-defined-v1",
    };
}
