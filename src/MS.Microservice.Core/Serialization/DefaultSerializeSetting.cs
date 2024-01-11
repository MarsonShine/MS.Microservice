using System.Text.Encodings.Web;
using System.Text.Json;

namespace MS.Microservice.Core.Serialization
{
    public class DefaultSerializeSetting
    {
        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static JsonSerializerOptions Default = jsonSerializerOptions;
    }
}
