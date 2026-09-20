using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Lab.AotExamples.Static.Persistence;

public sealed record ColumnDocument(string DisplayName, int Quantity);

// Independent implementation: lookup selects a previously supplied contract, never discovers members.
public sealed class JsonColumns(params JsonTypeInfo[] contracts)
{
    private readonly Dictionary<Type, JsonTypeInfo> contracts = contracts.ToDictionary(c => c.Type);

    public string Write(object value) => contracts.TryGetValue(value.GetType(), out var contract)
        ? JsonSerializer.Serialize(value, contract)
        : throw new NotSupportedException("Register this column's generated JSON contract first.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ColumnDocument))]
public partial class ColumnJsonContext : JsonSerializerContext;
