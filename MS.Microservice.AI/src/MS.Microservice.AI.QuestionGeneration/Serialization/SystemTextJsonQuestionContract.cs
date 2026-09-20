using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MS.Microservice.AI.QuestionGeneration.Contracts;

namespace MS.Microservice.AI.QuestionGeneration.Serialization;

public sealed class SystemTextJsonQuestionContract : IQuestionJsonContract
{
    private readonly ConcurrentDictionary<Type, JsonElement> schemas = new();
    private readonly IReadOnlyDictionary<Type, JsonTypeInfo> typeInfos;

    public SystemTextJsonQuestionContract(IEnumerable<JsonTypeInfo> typeInfos)
    {
        ArgumentNullException.ThrowIfNull(typeInfos);
        var context = new QuestionJsonContext(CreateOptions());
        var registered = new Dictionary<Type, JsonTypeInfo>
        {
            [typeof(QuestionDraftEnvelope)] = context.QuestionDraftEnvelope,
            [typeof(QuestionReviewEnvelope)] = context.QuestionReviewEnvelope,
            [typeof(QuestionRepairEnvelope)] = context.QuestionRepairEnvelope,
            [typeof(QuestionEvaluation)] = context.QuestionEvaluation,
        };
        foreach (var typeInfo in typeInfos)
        {
            ArgumentNullException.ThrowIfNull(typeInfo);
            var options = typeInfo.Options;
            if (options.AllowTrailingCommas || options.PropertyNameCaseInsensitive ||
                options.NumberHandling != JsonNumberHandling.Strict ||
                options.ReadCommentHandling != JsonCommentHandling.Disallow ||
                options.UnmappedMemberHandling != JsonUnmappedMemberHandling.Disallow)
            {
                throw new ArgumentException("Question metadata must use strict JSON options. Create the generated context with CreateOptions().", nameof(typeInfos));
            }
            typeInfo.MakeReadOnly();
            if (!registered.TryAdd(typeInfo.Type, typeInfo))
                throw new ArgumentException($"Question JSON metadata for '{typeInfo.Type}' is already registered.", nameof(typeInfos));
        }
        this.typeInfos = registered;
    }

    /// <summary>Creates strict options for a host's generated context. Register host enums with generic string enum converters.</summary>
    public static JsonSerializerOptions CreateOptions(params JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter<QuestionIssueSeverity>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<QuestionEvaluationDecision>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        foreach (var converter in converters) options.Converters.Add(converter);
        return options;
    }

    private JsonTypeInfo GetTypeInfo(Type type) => typeInfos.TryGetValue(type, out var typeInfo)
        ? typeInfo
        : throw new NotSupportedException($"No question JSON metadata is registered for '{type}'.");

    public JsonElement GetStrictSchema(Type responseType)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        return schemas.GetOrAdd(responseType, CreateStrictSchema);
    }

    public string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, GetTypeInfo(value.GetType()));
    }

    public JsonElement SerializeToElement(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToElement(value, GetTypeInfo(value.GetType()));
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

        return document.RootElement.Deserialize(GetTypeInfo(responseType))
            ?? throw new JsonException("Structured output deserialized to null.");
    }

    private JsonElement CreateStrictSchema(Type responseType)
    {
        var schema = GetTypeInfo(responseType).GetJsonSchemaAsNode(
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
                    required.Add(JsonValue.Create(property.Key));
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
