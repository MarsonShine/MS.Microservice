using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Lab.AotExamples.Static.AI;

public sealed record ChatPayload(string Model, string? Temperature, string[] Messages);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatPayload))]
public partial class ChatJsonContext : JsonSerializerContext;

public static class ChatJson
{
    public static string Serialize(ChatPayload request) => JsonSerializer.Serialize(request, ChatJsonContext.Default.ChatPayload);
    public static ChatPayload? Deserialize(string response) => JsonSerializer.Deserialize(response, ChatJsonContext.Default.ChatPayload);
}
