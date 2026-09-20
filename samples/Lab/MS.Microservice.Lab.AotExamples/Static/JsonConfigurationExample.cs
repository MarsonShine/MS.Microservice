using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MS.Microservice.Lab.AotExamples.Static;

/// <summary>每种协议复用自己的配置；已有缓存字节直接解析，HTTP 流继续异步读取。</summary>
public static class JsonConfigurationExample
{
    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly JsonSerializerOptions HttpOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static byte[] WriteCache(object value) => JsonSerializer.SerializeToUtf8Bytes(value, CacheOptions);

    public static T? ReadCache<T>(byte[] bytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> json = bytes;
        if (json.StartsWith(Encoding.UTF8.Preamble)) json = json[Encoding.UTF8.Preamble.Length..];
        var value = JsonSerializer.Deserialize<T>(json, CacheOptions);
        token.ThrowIfCancellationRequested();
        return value;
    }

    public static ValueTask<T?> ReadHttpAsync<T>(Stream stream, CancellationToken token = default)
        => JsonSerializer.DeserializeAsync<T>(stream, HttpOptions, token);
}
