using System.Text.Json;

namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// The response representation requested from a chat model.
/// </summary>
public enum AIChatResponseFormatKind
{
    Text = 0,
    JsonObject = 1,
    JsonSchema = 2,
}

/// <summary>
/// Provider-neutral response format for non-streaming chat requests.
/// </summary>
public sealed record AIChatResponseFormat
{
    private AIChatResponseFormat(
        AIChatResponseFormatKind kind,
        string? schemaName = null,
        JsonElement? schema = null,
        bool strict = true)
    {
        Kind = kind;
        SchemaName = schemaName;
        Schema = schema?.Clone();
        Strict = strict;
    }

    public AIChatResponseFormatKind Kind { get; }

    public string? SchemaName { get; }

    public JsonElement? Schema { get; }

    public bool Strict { get; }

    public static AIChatResponseFormat Text { get; } = new(AIChatResponseFormatKind.Text);

    public static AIChatResponseFormat JsonObject { get; } = new(AIChatResponseFormatKind.JsonObject);

    public static AIChatResponseFormat JsonSchema(string name, JsonElement schema, bool strict = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A JSON Schema response format requires an object schema.", nameof(schema));
        }

        return new(AIChatResponseFormatKind.JsonSchema, name.Trim(), schema, strict);
    }
}
