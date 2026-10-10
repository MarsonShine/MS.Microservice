using System.Buffers;
using System.Text;
using System.Text.Json;

namespace MS.Microservice.Core.Serialization;

public static class JsonText
{
    /// <summary>Formats one JSON value without applying application serialization policies.</summary>
    public static string Format(string json, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
