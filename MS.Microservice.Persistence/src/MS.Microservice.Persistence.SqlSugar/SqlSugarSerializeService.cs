using SqlSugar;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Persistence.SqlSugar;

/// <summary>SqlSugar JSON contracts are registered explicitly; unknown types never fall back to reflection.</summary>
public sealed class SqlSugarSerializeService : ISerializeService
{
    private readonly Dictionary<Type, JsonTypeInfo> contracts;

    public SqlSugarSerializeService(params JsonTypeInfo[] contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        this.contracts = new(contracts.Length);
        foreach (var contract in contracts)
        {
            ArgumentNullException.ThrowIfNull(contract);
            this.contracts.Add(contract.Type, contract);
        }
    }

    public T DeserializeObject<T>(string value) =>
        JsonSerializer.Deserialize(value, (JsonTypeInfo<T>)GetContract(typeof(T)))!;

    public string SerializeObject(object value) => value is null
        ? "null"
        : JsonSerializer.Serialize(value, GetContract(value.GetType()));

    public string SugarSerializeObject(object value) => SerializeObject(value);

    private JsonTypeInfo GetContract(Type type) => contracts.TryGetValue(type, out var contract)
        ? contract
        : throw new NotSupportedException($"SqlSugar JSON type '{type}' has no registered JsonTypeInfo contract.");
}
