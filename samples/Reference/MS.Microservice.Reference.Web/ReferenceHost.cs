using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MS.Microservice.AspNetCore;
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using Npgsql;

namespace MS.Microservice.Reference.Web;

public static class ReferenceHost
{
    private const string ApiRateLimitPolicy = "reference-api";

    public static void AddServices(WebApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("ReferenceDatabase");
        if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("ConnectionStrings:ReferenceDatabase is required.");
        var provider = builder.Configuration["Messaging:Provider"] ?? "SelfManaged";
        var topology = ReferenceMessages.Topology();
        var broker = builder.Configuration.GetSection("Messaging:RabbitMQ").Get<RabbitMqOptions>() ?? new();
        broker.Validate();
        if (string.Equals(provider, "SelfManaged", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseNpgsql(connection,
                postgres => postgres.MigrationsHistoryTable("__MigrationsHistory", ReferenceDbContext.Schema)));
            builder.Services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
            builder.Services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(topology,
                options => builder.Configuration.GetSection("Messaging:SelfManaged").Bind(options));
            builder.Services.AddRabbitMqTransport(broker);
        }
        else if (string.Equals(provider, "Wolverine", StringComparison.OrdinalIgnoreCase))
        {
            var settings = new WolverineMessagingOptions
            {
                ConnectionString = connection, BrokerConnectionString = broker.ConnectionString,
                Exchange = broker.Exchange, QueuePrefix = broker.QueuePrefix
            };
            builder.Host.UseWolverineMessaging<WolverineReferenceDbContext>(topology, settings);
            builder.Services.AddReferenceRepositories<WolverineReferenceDbContext>();
        }
        else throw new ArgumentException("Messaging:Provider must be SelfManaged or Wolverine.");
        builder.Services.AddExceptionHandler<ReferenceConflictHandler>();
        builder.Services.AddPlatformHttp(builder.Configuration).AddExternalIdentity(builder.Configuration, builder.Environment);
        if (RateLimitingEnabled(builder.Configuration))
        {
            var permitLimit = PositiveInt(builder.Configuration, "Http:RateLimiting:PermitLimit", 120);
            var windowSeconds = PositiveInt(builder.Configuration, "Http:RateLimiting:WindowSeconds", 60);
            builder.Services.AddPlatformRateLimiting(options => options.AddFixedWindowLimiter(ApiRateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = permitLimit;
                limiter.Window = TimeSpan.FromSeconds(windowSeconds);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            }));
        }
        builder.Services.AddMsRequestLogging();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        if (builder.Configuration.GetValue("OpenTelemetry:Enabled", true)) builder.Services.AddMsOpenTelemetry(builder.Configuration);
    }

    public static void MapApplication(WebApplication app)
    {
        var rateLimiting = RateLimitingEnabled(app.Configuration);
        app.UsePlatformHttp();
        app.UseMsRequestLogging();
        app.UseAuthentication();
        if (rateLimiting) app.UsePlatformRateLimiting();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
        app.MapGet("/health/ready", ReadinessAsync).AllowAnonymous();
        ProfileEndpoints.Map(rateLimiting ? app.MapGroup("").RequireRateLimiting(ApiRateLimitPolicy) : app);
    }

    private static bool RateLimitingEnabled(IConfiguration configuration)
    {
        var setting = configuration["Http:RateLimiting:Enabled"];
        if (setting is null) return false;
        if (bool.TryParse(setting, out var enabled)) return enabled;
        throw new ArgumentException("Http:RateLimiting:Enabled must be true or false.");
    }

    private static int PositiveInt(IConfiguration configuration, string key, int fallback)
    {
        var setting = configuration[key];
        if (setting is null) return fallback;
        if (int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            return value;
        throw new ArgumentException($"{key} must be a positive integer.");
    }

    private static async Task<IResult> ReadinessAsync(ReferenceDbContext context, IMessageStorageProbe storage,
        RabbitMqTransport broker, MessagingProviderRegistration provider, CancellationToken cancellationToken)
    {
        try
        {
            await context.Profiles.AsNoTracking().AnyAsync(cancellationToken);
            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return Results.Json(new { status = "unhealthy", reason = "pending_migrations" }, statusCode: 503);
            await storage.CheckAsync(cancellationToken);
            var connected = await broker.ProbeAsync(cancellationToken);
            return Results.Ok(new { status = connected ? "healthy" : "degraded", messaging = provider.Name });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Results.Json(new { status = "unhealthy", reason = "storage_unavailable" }, statusCode: 503); }
    }
}

internal sealed class ReferenceConflictHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException && exception is not DbUpdateException
            { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }) return false;
        context.Response.StatusCode = 409;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new() { Status = 409, Title = "The record changed or the external identity already exists." }
        });
    }
}
