using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MS.Microservice.Core.Serialization.Converters
{
    internal class StringLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? stringValue = reader.GetString();
                if (long.TryParse(stringValue, out long value))
                {
                    return value;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt64();
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
