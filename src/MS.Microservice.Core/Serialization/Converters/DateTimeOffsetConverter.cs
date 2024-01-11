using MS.Microservice.Core.Extension;
using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MS.Microservice.Core.Serialization.Converters
{
    public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var time = reader.GetString();

            return time.IsNullOrEmpty() ? default : new DateTimeOffset(DateTimeOffset.Parse(time).DateTime, TimeSpan.FromHours(8));
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }

    public class StringDateTimeOffsetConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.IsNullOrEmpty())
                writer.WriteStringValue(default(DateTimeOffset));
            else
                writer.WriteStringValue(new DateTimeOffset(DateTimeOffset.Parse(value).DateTime, TimeSpan.FromHours(8)));
        }
    }
}
