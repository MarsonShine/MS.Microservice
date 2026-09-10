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

        try
        {
            await _next(context);
        }
        finally
        {
            requestLogContext.StatusCode = context.Response?.StatusCode;
            requestLogContext.ElapsedMilliseconds = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (_options.EmitCompletionLog && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogHttpRequest(
                    requestLogContext.Method,
                    requestLogContext.Path ?? string.Empty,
                    requestLogContext.StatusCode ?? 0,
                    requestLogContext.ElapsedMilliseconds ?? 0L);
            }
        }
    }

    private RequestLogContext CreateRequestLogContext(HttpContext context)
    {
        var headers = context.Request.Headers;

        return new RequestLogContext
        {
            RequestId = TryGetHeaderValue(headers, _options.RequestIdHeaderName),
            PlatformId = TryGetHeaderValue(headers, _options.PlatformIdHeaderName),
            UserFlag = TryGetHeaderValue(headers, _options.UserFlagHeaderName),
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
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