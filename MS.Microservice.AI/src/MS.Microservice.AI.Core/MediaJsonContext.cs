using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.AI.Core;

internal sealed record SpeechPayload(string Model, string Input, string Voice,
    [property: JsonPropertyName("response_format")] string ResponseFormat, double? Speed);

internal sealed record ImageGenerationPayload(string Model, string Prompt, int N, string? Size, string? Quality,
    [property: JsonPropertyName("response_format")] string ResponseFormat);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpeechPayload))]
[JsonSerializable(typeof(ImageGenerationPayload))]
internal partial class MediaJsonContext : JsonSerializerContext;
