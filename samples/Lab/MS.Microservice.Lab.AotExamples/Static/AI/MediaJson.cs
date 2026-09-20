using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Lab.AotExamples.Static.AI;

public sealed record SpeechPayload(string Model, string Input, string Voice,
    [property: JsonPropertyName("response_format")] string ResponseFormat, double? Speed);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpeechPayload))]
public partial class MediaJsonContext : JsonSerializerContext;

public static class MediaJson
{
    public static string SerializeSpeech(string model, string input, string voice, double? speed) =>
        JsonSerializer.Serialize(new SpeechPayload(model, input, voice, "mp3", speed), MediaJsonContext.Default.SpeechPayload);
}
