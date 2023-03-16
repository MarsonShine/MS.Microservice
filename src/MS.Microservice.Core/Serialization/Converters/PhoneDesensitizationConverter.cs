using MS.Microservice.Core.Extension;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Core.Serialization.Converters
{
    public class PhoneDesensitizationConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var phone = reader.GetString();
                return phone;
            }

            return reader.GetString();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            var phone = value;
            if (!value.IsNullOrEmpty() && value.Length == 11)
            {
                phone = value.Remove(3, 4)
                .Insert(3, "****");
            }

            writer.WriteStringValue(phone);
        }
    }
}
