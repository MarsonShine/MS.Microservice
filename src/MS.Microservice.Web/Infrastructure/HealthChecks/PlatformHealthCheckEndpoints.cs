using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MS.Microservice.Infrastructure.HealthChecks;

namespace MS.Microservice.Web.Infrastructure.HealthChecks;

public static class PlatformHealthCheckEndpoints
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";
    public const string CompatibilityPath = "/hc";

    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivenessPath, CreateOptions("live"));
        endpoints.MapHealthChecks(ReadinessPath, CreateOptions(SqlHealthCheck.ReadinessTag));
        endpoints.MapHealthChecks(CompatibilityPath, CreateOptions(SqlHealthCheck.ReadinessTag));
        return endpoints;
    }

    public static HealthCheckOptions CreateOptions(string requiredTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredTag);
        return new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(requiredTag),
            ResponseWriter = WriteResponseAsync
        };
    }

    public static async Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
                })
        }, context.RequestAborted);
    }
}
