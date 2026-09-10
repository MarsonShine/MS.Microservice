using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
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
        IEnumerable<KeyValuePair<string, object?>> pairs = body is IDictionary dictionary
            ? dictionary.Keys.Cast<object>().Select(key => new KeyValuePair<string, object?>(
                Convert.ToString(key, CultureInfo.InvariantCulture) ?? "", dictionary[key]))
            : body.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
                .Select(property => new KeyValuePair<string, object?>(property.Name, property.GetValue(body)));
        var parameters = new List<string>();
        foreach (var (name, value) in pairs)
        {
            if (value is IEnumerable values and not string)
                foreach (var item in values) Add(name, item);
            else Add(name, value);
        }
        if (parameters.Count == 0) return url;
        var fragmentIndex = url.IndexOf('#');
        var path = fragmentIndex < 0 ? url : url[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
        var separator = path.Contains('?') ? (path.EndsWith('?') || path.EndsWith('&') ? "" : "&") : "?";
        return path + separator + string.Join('&', parameters) + fragment;

        void Add(string name, object? value)
        {
            if (value is null) return;
            var text = value switch
            {
                DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            parameters.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text ?? ""));
        }
    }
}
