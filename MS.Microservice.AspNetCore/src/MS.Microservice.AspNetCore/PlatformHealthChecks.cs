using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace MS.Microservice.AspNetCore;

public static class PlatformHealthChecks
{
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddPlatformHealthChecks(this IServiceCollection services)
        => services.AddHealthChecks();

    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, HealthReport, Task>? writeReadyResponse = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", (RequestDelegate)(context => WriteStatusAsync(context, "healthy")))
            .AllowAnonymous().DisableRateLimiting().DisableRequestTimeout();
        endpoints.MapGet("/health/ready", (RequestDelegate)(async context =>
        {
            var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
            if (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await WriteStatusAsync(context, "unhealthy", "stopping");
                return;
            }

            var checks = context.RequestServices.GetRequiredService<HealthCheckService>();
            var report = await checks.CheckHealthAsync(static registration => registration.Tags.Contains(ReadyTag),
                context.RequestAborted);
            context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
            if (writeReadyResponse is null)
                await WriteStatusAsync(context, report.Status switch
                {
                    HealthStatus.Healthy => "healthy",
                    HealthStatus.Degraded => "degraded",
                    _ => "unhealthy"
                });
            else await writeReadyResponse(context, report);
        })).AllowAnonymous().DisableRateLimiting().DisableRequestTimeout();

        return endpoints;
    }

    private static async Task WriteStatusAsync(HttpContext context, string status, string? reason = null)
    {
        context.Response.ContentType = "application/json";
        using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("status", status);
        if (reason is not null) writer.WriteString("reason", reason);
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }
}
