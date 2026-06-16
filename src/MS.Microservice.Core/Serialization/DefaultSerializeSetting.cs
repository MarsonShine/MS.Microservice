using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace MS.Microservice.Core.Serialization
{
    public class DefaultSerializeSetting
    {
        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 中文处理
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public static JsonSerializerOptions Default = jsonSerializerOptions;

        public static JavaScriptEncoder ChineseEncoder => JavaScriptEncoder.Create(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.CjkUnifiedIdeographs,
            UnicodeRanges.CjkUnifiedIdeographsExtensionA,
            UnicodeRanges.CjkSymbolsandPunctuation,
            UnicodeRanges.HalfwidthandFullwidthForms);
    }
}
