using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Net.Http
{
    /// <summary>
    /// 通过 IHttpClientFactory 注入 LoggingHttpClientHandler
    /// 解决调用段重复读取 HttpContent 的问题
    /// </summary>
    public class LoggingHttpClientHandler(ILogger<LoggingHttpClientHandler> logger) : DelegatingHandler
    {
        private readonly ILogger<LoggingHttpClientHandler> _logger = logger;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();

            // 包装请求内容
            if (request.Content != null && !IsLoggableContent(request.Content))
            {
                request.Content = new LoggableHttpContent(request.Content);
            }

            _logger.LogInformation(
                "【{Guid}】method:【{Method}】log request: 【{RequestUri}】 payload: 【{@Payload}】",
                guid,
                request.Method.Method,
                request.RequestUri,
                new LazyContentLogger(request.Content, cancellationToken)
            );

            var response = await base.SendAsync(request, cancellationToken);

            // 包装响应内容
            if (response.Content != null && !IsLoggableContent(response.Content))
            {
                response.Content = new LoggableHttpContent(response.Content);
            }

            _logger.LogInformation(
                "【{Guid}】method:【{Method}】log response: 【{@Response}】",
                guid,
                request.Method.Method,
                new LazyContentLogger(response.Content, cancellationToken)
            );

            return response;
        }

        private static bool IsLoggableContent(HttpContent content) => content is LoggableHttpContent;

        public sealed class LazyContentLogger(HttpContent? httpContent, CancellationToken cancellationToken)
        {
            public override string ToString()
            {
                if (httpContent == null)
                    return "null";

                if (httpContent is LoggableHttpContent loggable)
                {
                    try
                    {
                        return loggable.GetCachedContentAsync(cancellationToken)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (Exception ex)
                    {
                        return $"Error reading content: {ex.Message}";
                    }
                }

                return "[Non-loggable content]";
            }
        }

        public class LoggableHttpContent : HttpContent
        {
            private readonly HttpContent _originalContent;
            private byte[]? _cachedBytes;
            private readonly SemaphoreSlim _cacheLock = new(1, 1);

            public LoggableHttpContent(HttpContent originalContent)
            {
                _originalContent = originalContent;

                // 复制所有 headers
                foreach (var header in originalContent.Headers)
                {
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            public async Task<string> GetCachedContentAsync(CancellationToken cancellationToken = default)
            {
                var bytes = await GetCachedBytesAsync(cancellationToken);
                return Encoding.UTF8.GetString(bytes);
            }

            private async Task<byte[]> GetCachedBytesAsync(CancellationToken cancellationToken = default)
            {
                if (_cachedBytes != null)
                    return _cachedBytes;

                await _cacheLock.WaitAsync(cancellationToken);
                try
                {
                    _cachedBytes ??= await _originalContent.ReadAsByteArrayAsync(cancellationToken);
                    return _cachedBytes;
                }
                finally
                {
                    _cacheLock.Release();
                }
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                var bytes = await GetCachedBytesAsync();
                await stream.WriteAsync(bytes.AsMemory(), CancellationToken.None);
            }

            protected override bool TryComputeLength(out long length)
            {
                if (_cachedBytes != null)
                {
                    length = _cachedBytes.Length;
                    return true;
                }
                length = -1;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _originalContent?.Dispose();
                    _cacheLock?.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
