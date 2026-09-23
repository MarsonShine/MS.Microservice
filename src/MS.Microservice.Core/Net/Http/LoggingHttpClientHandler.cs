using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MS.Microservice.Core.Net.Http;

/// <summary>Logs HTTP bodies asynchronously, or metadata only when redaction is enabled.</summary>
public class LoggingHttpClientHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHttpClientHandler> _logger;
    private readonly LoggingHttpClientHandlerOptions _options;

    public LoggingHttpClientHandler(ILogger<LoggingHttpClientHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = new LoggingHttpClientHandlerOptions();
    }

    public LoggingHttpClientHandler(ILogger<LoggingHttpClientHandler> logger,
        IOptions<LoggingHttpClientHandlerOptions> options) : this(logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (_options.EnableRedaction)
            return await SendRedactedAsync(request, cancellationToken).ConfigureAwait(false);

        var id = Guid.NewGuid();
        var requestBody = await ReadContentForLogAsync(request.Content, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "【{Guid}】method:【{Method}】log request: 【{RequestUri}】 payload: 【{@Payload}】",
            id, request.Method.Method, request.RequestUri, requestBody);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var responseBody = await ReadContentForLogAsync(response.Content, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "【{Guid}】method:【{Method}】log response: 【{@Response}】",
                id, request.Method.Method, responseBody);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendRedactedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var path = PathWithoutQuery(request.RequestUri);
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("HTTP {Method} {Path} canceled in {ElapsedMilliseconds} ms",
                request.Method.Method, path, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogInformation("HTTP {Method} {Path} failed {FailureType} in {ElapsedMilliseconds} ms",
                request.Method.Method, path, exception.GetType().Name,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }

        try
        {
            _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                request.Method.Method, path, (int)response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadContentForLogAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content is null) return "null";
        try
        {
            var text = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return $"Error reading content: {exception.Message}";
        }
    }

    private static string PathWithoutQuery(Uri? uri)
    {
        if (uri is null) return string.Empty;
        if (uri.IsAbsoluteUri) return uri.AbsolutePath;

        var text = uri.OriginalString;
        var query = text.IndexOf('?');
        var fragment = text.IndexOf('#');
        var end = query < 0 ? fragment : fragment < 0 ? query : Math.Min(query, fragment);
        return end < 0 ? text : text[..end];
    }
}
