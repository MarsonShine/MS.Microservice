using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Net.Http
{
    // TODO: 日志单独配置化，可以单独控制是否记录日志
    public class LogHttpClient
    {
        private readonly ILogger<LogHttpClient> _logger;
        private readonly HttpClient _httpClient;

        public JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public LogHttpClient(ILogger<LogHttpClient> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public void Configure(string baseAddress, TimeSpan timeout)
        {
            _httpClient.BaseAddress = new Uri(baseAddress);
            _httpClient.Timeout = timeout;
        }

        public async ValueTask<T?> GetAsync<T>(string requestUrl, object body, CancellationToken cancellationToken = default)
        {
            string query = BuildQuery(body);
            var guid = Guid.NewGuid();
            _logger.LogInformation("【{Guid}】method:【GET】log request: 【{query}】", guid, $"{requestUrl}?{query}");
            using var response = await _httpClient.GetAsync($"{requestUrl}?{query}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            try
            {
                var result = await JsonSerializer.DeserializeAsync<T>(contentStream, options: JsonSerializerOptions, cancellationToken);
                _logger.LogInformation("【{Guid}】method:【GET】log response: 【{result}】", guid, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("【{Guid}】method:【GET】log request error: 【{error}】", guid, ex.Message + Environment.NewLine + ex.StackTrace);
                throw new Exception("服务器数据解析异常", ex);
            }
        }

        public async ValueTask<T?> GetAsync<T>(string url, object body, Dictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            SetHeaders(headers);
            return await GetAsync<T>(url, body, cancellationToken);
        }

        private static string BuildQuery(object body)
        {
            var queryParameters = body.GetType().GetTypeInfo()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name + "=" + p.GetValue(body))
                .ToArray();

            return string.Join('&', queryParameters);
        }

        public async ValueTask<T?> PostAsync<T>(string url, object body, CancellationToken cancellationToken = default)
        {
            var guid = Guid.NewGuid();
            _logger.LogInformation("【{Guid}】method:【POST】log request: 【{body}】", guid, body);
            try
            {

                using var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions)), cancellationToken);
                response.EnsureSuccessStatusCode();
                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<T>(contentStream, options: JsonSerializerOptions, cancellationToken);
                _logger.LogInformation("【{Guid}】method:【POST】log response: 【{result}】", guid, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("【{Guid}】method:【POST】log request error: 【{error}】", guid, ex.Message + Environment.NewLine + ex.StackTrace);
                throw new Exception("服务器数据解析异常", ex);
            }
        }

        public async Task<T?> PostAsync<T>(string url, object body, Dictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            SetHeaders(headers);
            return await PostAsync<T>(url, body, cancellationToken);
        }

        private void SetHeaders(Dictionary<string, string>? headers)
        {
            if (headers?.Count > 0)
            {
                foreach (var (key, value) in headers)
                {
                    _httpClient.DefaultRequestHeaders.Add(key, value);
                }
            }
        }
    }
}
