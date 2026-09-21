using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Lab.AotExamples.Static.CoreJson;

public static class JsonRoundTrip
{
    public static T? Copy<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), typeInfo);
}
