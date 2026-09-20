using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Lab.AotExamples.Legacy.AI;

// The previous production mechanism: reusable options still discover DTO members at runtime.
public static class ChatJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T request) => JsonSerializer.Serialize(request, Options);
    public static T? Deserialize<T>(string response) => JsonSerializer.Deserialize<T>(response, Options);
}
