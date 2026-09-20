using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Lab.AotExamples.Legacy.AI;

public static class MediaJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeSpeech(string model, string input, string voice, double? speed)
    {
        object payload = new { model, input, voice, response_format = "mp3", speed };
        return JsonSerializer.Serialize(payload, Options);
    }
}
