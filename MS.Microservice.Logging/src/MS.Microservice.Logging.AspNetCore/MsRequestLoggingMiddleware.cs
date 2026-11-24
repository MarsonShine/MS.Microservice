using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Logging.Core;

namespace MS.Microservice.Logging.AspNetCore;

/// <summary>
/// Captures request metadata once, stores it in the ambient request log scope, and emits a completion log.
/// </summary>
public sealed class MsRequestLoggingMiddleware(
    RequestDelegate next,
    TimeProvider timeProvider,
    ILogger<MsRequestLoggingMiddleware> logger,
    IOptions<AspNetCoreRequestLoggingOptions> options)
{
    private static readonly Action<ILogger, string?, string, long, int?, string?, Exception?> FailedRequest =
        LoggerMessage.Define<string?, string, long, int?, string?>(LogLevel.Warning, new EventId(5001, "HttpRequestFailed"),
            "HTTP {Method} {Path} failed in {ElapsedMs}ms; status {StatusCode}; failure {FailureType}");
    private static readonly Action<ILogger, string?, string, long, int?, string?, Exception?> CanceledRequest =
        LoggerMessage.Define<string?, string, long, int?, string?>(LogLevel.Information, new EventId(5002, "HttpRequestCanceled"),
            "HTTP {Method} {Path} canceled in {ElapsedMs}ms; status {StatusCode}; failure {FailureType}");

    private readonly RequestDelegate _next = next;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<MsRequestLoggingMiddleware> _logger = logger;
    private readonly AspNetCoreRequestLoggingOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestLogContext = CreateRequestLogContext(context);
        var startTimestamp = _timeProvider.GetTimestamp();

        using var _ = RequestLogScope.Push(requestLogContext);
        string? failureType = null;

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            failureType = exception.GetType().Name;
            throw;
        }
        finally
        {
            var canceled = context.RequestAborted.IsCancellationRequested;
            var response = context.Response;
            int? statusCode = (failureType is not null || canceled) && !response.HasStarted
                ? null : response.StatusCode;
            requestLogContext.StatusCode = statusCode;
            var elapsed = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            requestLogContext.ElapsedMilliseconds = elapsed;

            if (_options.EmitCompletionLog)
            {
                var path = requestLogContext.Path ?? string.Empty;
                if (canceled)
                {
                    CanceledRequest(_logger, requestLogContext.Method, path, elapsed, statusCode, failureType, null);
                }
                else if (failureType is not null)
                {
                    FailedRequest(_logger, requestLogContext.Method, path, elapsed, statusCode, failureType, null);
                }
                else
                {
                    _logger.LogHttpRequest(requestLogContext.Method, path, statusCode!.Value, elapsed);
                }
            }
        }
    }

    private RequestLogContext CreateRequestLogContext(HttpContext context)
    {
        var request = context.Request;
        var headers = request.Headers;

        return new RequestLogContext
        {
            RequestId = TryGetHeaderValue(headers, _options.RequestIdHeaderName),
            PlatformId = TryGetHeaderValue(headers, _options.PlatformIdHeaderName),
            UserFlag = TryGetHeaderValue(headers, _options.UserFlagHeaderName),
            Method = request.Method,
            Path = request.Path.Value ?? string.Empty,
        };
    }

    private static string? TryGetHeaderValue(IHeaderDictionary headers, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        return headers.TryGetValue(headerName, out var values) && values.Count > 0
            ? values[0]
            : null;
    }
}
