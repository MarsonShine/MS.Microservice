using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Core.Serialization;

/// <summary>An explicit set of JSON contracts; missing types never fall back to reflection.</summary>
public sealed class JsonTypeRegistry
{
    private readonly FrozenDictionary<Type, JsonTypeInfo> _types;

    public JsonTypeRegistry(params JsonTypeInfo[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var contracts = new Dictionary<Type, JsonTypeInfo>();
        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            contracts.Add(type.Type, type);
        }
        _types = contracts.ToFrozenDictionary();
    }

    public JsonTypeInfo Get(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _types.TryGetValue(type, out var info) ? info
            : throw new NotSupportedException($"JSON contract '{type}' is not registered.");
    }

    public JsonTypeInfo<T> Get<T>() => (JsonTypeInfo<T>)Get(typeof(T));
}
