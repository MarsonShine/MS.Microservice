using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.AI.QuestionGeneration.Serialization;

public sealed class SystemTextJsonQuestionContract : IQuestionJsonContract
{
    private readonly ConcurrentDictionary<Type, JsonElement> schemas = new();
    private readonly JsonSerializerOptions serializerOptions;

    public SystemTextJsonQuestionContract()
    {
        serializerOptions = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        serializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        serializerOptions.MakeReadOnly();
    }

    public JsonElement GetStrictSchema(Type responseType)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        return schemas.GetOrAdd(responseType, CreateStrictSchema);
    }

    public string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, value.GetType(), serializerOptions);
    }

    public object Deserialize(string response, Type responseType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        ArgumentNullException.ThrowIfNull(responseType);

        var trimmed = response.AsSpan().Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            throw new JsonException("Markdown code fences are not valid structured output.");
        }

        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(response),
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        using var document = JsonDocument.ParseValue(ref reader);
        if (reader.Read())
        {
            throw new JsonException("Structured output contains content after the root JSON value.");
        }

        return document.RootElement.Deserialize(responseType, serializerOptions)
            ?? throw new JsonException("Structured output deserialized to null.");
    }

    private JsonElement CreateStrictSchema(Type responseType)
    {
        var schema = serializerOptions.GetJsonSchemaAsNode(
            responseType,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            });
        MakeObjectsStrict(schema);
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void MakeObjectsStrict(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject["properties"] is JsonObject properties)
            {
                jsonObject["additionalProperties"] = false;
                var required = new JsonArray();
                foreach (var property in properties)
                {
                    required.Add(property.Key);
                }

                jsonObject["required"] = required;
            }

            foreach (var property in jsonObject.ToArray())
            {
                MakeObjectsStrict(property.Value);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                MakeObjectsStrict(item);
            }
        }
    }
}
