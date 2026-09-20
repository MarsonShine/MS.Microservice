using System.Text.Json;

namespace MS.Microservice.Lab.AotExamples.Legacy.Persistence;

public static class JsonColumns
{
    public static string Write(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}
