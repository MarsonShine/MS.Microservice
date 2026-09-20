using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Core.Net.Http;

/// <summary>保持 HTTP 编码、请求头隔离及原始取消/失败语义的通用 JSON 请求辅助。</summary>
/// <remarks>业务重试由调用方决定；默认诊断不记录 URL 参数或正文，避免把凭据和业务数据带进日志。</remarks>
public class LogHttpClient(ILogger<LogHttpClient> logger, HttpClient httpClient)
{
    public JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public void Configure(string baseAddress, TimeSpan timeout)
    {
        httpClient.BaseAddress = new Uri(baseAddress);
        httpClient.Timeout = timeout;
    }

    public ValueTask<T?> GetAsync<T>(string requestUrl, object? body, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, BuildUrl(requestUrl, body), null, null, cancellationToken);

    public ValueTask<T?> GetAsync<T>(string url, object? body, Dictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, BuildUrl(url, body), null, headers, cancellationToken);

    public ValueTask<T?> PostAsync<T>(string url, object? body, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Post, url, body, null, cancellationToken);

    public Task<T?> PostAsync<T>(string url, object? body, Dictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Post, url, body, headers, cancellationToken).AsTask();

    private async ValueTask<T?> SendAsync<T>(HttpMethod method, string url, object? body,
        Dictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(method, url);
        if (method == HttpMethod.Post)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions), Encoding.UTF8, "application/json");
        if (headers is not null)
            foreach (var (name, value) in headers) request.Headers.Add(name, value);

        var id = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("HTTP {RequestId} {Method} started", id, method.Method);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonSerializerOptions, cancellationToken);
            logger.LogInformation("HTTP {RequestId} {Method} completed {StatusCode} in {ElapsedMs} ms",
                id, method.Method, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            // URLs, bodies and exception messages may contain credentials or personal data.
            logger.LogInformation("HTTP {RequestId} {Method} ended {FailureType} in {ElapsedMs} ms",
                id, method.Method, exception.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private static string BuildUrl(string url, object? body)
    {
        if (body is null) return url;
        var parameters = new List<string>();
        // 参数写入统一交给 QueryStringParameters：字典与对象两条路径共用同一套值语义。
        var before = parameters.Count;
        if (QueryStringParameters.Dispatch(body, parameters) == before) return url;

        var fragmentIndex = url.IndexOf('#');
        var path = fragmentIndex < 0 ? url : url[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
        var separator = path.Contains('?') ? (path.EndsWith('?') || path.EndsWith('&') ? "" : "&") : "?";
        return string.Concat(path, separator, string.Join('&', parameters), fragment);
    }
}
