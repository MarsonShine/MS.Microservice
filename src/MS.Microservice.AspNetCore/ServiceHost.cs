using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.AspNetCore;

public static class ServiceHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders().AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });
        return builder;
    }

    public static IServiceCollection AddPlatformHttp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<UnhandledExceptionHandler>();
        services.Configure<ExceptionHandlerOptions>(options => options.SuppressDiagnosticsCallback = _ => true);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var address in configuration.GetSection("Http:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(address));
        });
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
        return services;
    }

    public static WebApplication UsePlatformHttp(this WebApplication application)
    {
        application.UseExceptionHandler();
        application.UseStatusCodePages();
        application.UseForwardedHeaders();
        application.UseRouting();
        application.UseCors();
        return application;
    }

    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services, Action<RateLimiterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static (context, _) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString("0", CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            configure(options);
        });
        return services;
    }

    public static WebApplication UsePlatformRateLimiting(this WebApplication application)
    {
        application.UseRateLimiter();
        return application;
    }
}

internal sealed class UnhandledExceptionHandler(IProblemDetailsService problems, ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return true;
        logger.LogError("Unhandled {ExceptionType}; trace {TraceId}; stack {StackTrace}",
            exception.GetType().Name, context.TraceIdentifier, exception.StackTrace);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new() { Status = 500, Title = "An unexpected error occurred." }
        });
    }
}
