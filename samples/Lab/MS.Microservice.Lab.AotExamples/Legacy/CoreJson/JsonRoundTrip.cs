using System.Text.Json;

namespace MS.Microservice.Lab.AotExamples.Legacy.CoreJson;

public static class JsonRoundTrip
{
    public static T? Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value));
}
