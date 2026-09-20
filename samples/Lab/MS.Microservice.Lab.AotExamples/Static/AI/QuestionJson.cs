using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Lab.AotExamples.Static.AI;

public sealed class RegisteredQuestionJson(params JsonTypeInfo[] typeInfos)
{
    private readonly Dictionary<Type, JsonTypeInfo> registrations = typeInfos.ToDictionary(info => info.Type);
    private JsonTypeInfo GetInfo(Type type) => registrations.TryGetValue(type, out var info)
        ? info : throw new NotSupportedException($"No JSON metadata for {type}.");

    public string Serialize(object value) => JsonSerializer.Serialize(value, GetInfo(value.GetType()));
    public object? Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, GetInfo(type));
}

public enum QuestionDifficulty { Basic, Advanced }
public sealed record ExampleQuestion(string Stem, QuestionDifficulty Difficulty);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ExampleQuestion))]
public partial class QuestionExampleJsonContext : JsonSerializerContext;
