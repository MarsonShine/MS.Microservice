using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.AspNetCore.Encryption;

public sealed class ApiEncryptedModelTypes
{
    private readonly Dictionary<Type, JsonTypeInfo> _types = new();

    public void Add<T>(JsonTypeInfo<T> typeInfo) where T : IApiEncrypt
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        _types.Add(typeof(T), typeInfo);
    }

    internal bool TryGet(Type modelType, out JsonTypeInfo typeInfo)
        => _types.TryGetValue(modelType, out typeInfo!);
}
