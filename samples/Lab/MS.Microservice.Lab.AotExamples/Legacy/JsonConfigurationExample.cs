using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MS.Microservice.Lab.AotExamples.Legacy;

/// <summary>截取原缓存及 HTTP 辅助类的 JSON 读写路径，保留逐次初始化和内存流包装。</summary>
public static class JsonConfigurationExample
{
    public static byte[] WriteCache(object value) => JsonSerializer.SerializeToUtf8Bytes(value,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        });

    public static ValueTask<T?> ReadCacheAsync<T>(byte[] bytes, CancellationToken token = default)
        => JsonSerializer.DeserializeAsync<T>(new MemoryStream(bytes), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        }, token);

    public static ValueTask<T?> ReadHttpAsync<T>(Stream stream, CancellationToken token = default)
        => JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        }, token);
}
