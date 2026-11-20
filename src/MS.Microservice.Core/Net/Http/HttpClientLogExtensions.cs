using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Core.Net.Http;

internal static class HttpClientLogExtensions
{
    internal static void HttpRequestStarted(this ILogger logger, Guid requestId, HttpMethod method)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;

        logger.LogInformation("HTTP {RequestId} {Method} started", requestId, method.Method);
    }

    internal static void HttpRequestCompleted(this ILogger logger, Guid requestId, HttpMethod method,
        HttpStatusCode statusCode, long started)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;

        logger.LogInformation("HTTP {RequestId} {Method} completed {StatusCode} in {ElapsedMs} ms",
            requestId, method.Method, (int)statusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    internal static void HttpRequestFailed(this ILogger logger, Guid requestId, HttpMethod method,
        Exception exception, long started)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;

        // Exception messages may contain request URLs or credentials. Record only the failure type.
        logger.LogInformation("HTTP {RequestId} {Method} ended {FailureType} in {ElapsedMs} ms",
            requestId, method.Method, exception.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
