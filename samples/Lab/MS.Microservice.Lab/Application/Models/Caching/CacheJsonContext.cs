using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MS.Microservice.Lab.Application.Models.Caching;

[JsonSerializable(typeof(UserCacheItem))]
internal partial class CacheJsonContext : JsonSerializerContext
{
    internal static CacheJsonContext Instance { get; } = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });
}
