using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MS.Microservice.AspNetCore;
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Idempotency.EFCore;
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
    private const string ApiTimeoutPolicy = "reference-api-timeout";

    public static void AddServices(WebApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("ReferenceDatabase");
        if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("ConnectionStrings:ReferenceDatabase is required.");
        var provider = builder.Configuration["Messaging:Provider"] ?? "SelfManaged";
        var idempotency = new ReferenceIdempotencyOptions(Enabled(builder.Configuration, "Http:Idempotency:Enabled"));
        builder.Services.AddSingleton(idempotency);
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
        if (idempotency.Enabled)
        {
            builder.Services.AddScoped(services => new EfCoreIdempotencyStore<ReferenceDbContext>(
                services.GetRequiredService<ReferenceDbContext>(), services.GetRequiredService<TimeProvider>()));
            builder.Services.AddScoped<ReferenceHttpIdempotencyExecutor>();
            builder.Services.AddHostedService<ReferenceIdempotencyCleanupWorker>();
        }
        builder.Services.AddExceptionHandler<ReferenceConflictHandler>();
        builder.Services.AddPlatformHttp(builder.Configuration).AddExternalIdentity(builder.Configuration, builder.Environment);
        builder.Services.AddValidation();
        builder.Services.AddPlatformHealthChecks()
            .Add(new HealthCheckRegistration("durable-storage",
                static services => new ReferenceDurableStorageHealthCheck(
                    services.GetRequiredService<ReferenceDbContext>(), services.GetRequiredService<IMessageStorageProbe>(),
                    services.GetRequiredService<ReferenceIdempotencyOptions>()),
                HealthStatus.Unhealthy, [PlatformHealthChecks.ReadyTag], TimeSpan.FromSeconds(5)))
            .Add(new HealthCheckRegistration("broker",
                static services => new ReferenceBrokerHealthCheck(services.GetRequiredService<RabbitMqTransport>()),
                HealthStatus.Unhealthy, [PlatformHealthChecks.ReadyTag], TimeSpan.FromSeconds(5)));
        if (Enabled(builder.Configuration, "Http:RateLimiting:Enabled"))
        {
            var permitLimit = PositiveInt(builder.Configuration, "Http:RateLimiting:PermitLimit", 120);
            var windowSeconds = PositiveInt(builder.Configuration, "Http:RateLimiting:WindowSeconds", 60);
            var subjectClaimType = builder.Configuration["Authentication:SubjectClaimType"] ?? "sub";
            builder.Services.AddPlatformRateLimiting(options => options.AddPolicy<(string Issuer, string Subject)>(
                ApiRateLimitPolicy, http =>
            {
                var issuer = http.User.FindFirstValue("iss");
                var subject = http.User.FindFirstValue(subjectClaimType);
                (string Issuer, string Subject) partition = http.User.Identity?.IsAuthenticated == true
                    && !string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject)
                    ? (issuer, subject) : (string.Empty, string.Empty);
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = false
                });
            }));
        }
        if (Enabled(builder.Configuration, "Http:RequestTimeouts:Enabled"))
        {
            var seconds = PositiveInt(builder.Configuration, "Http:RequestTimeouts:Seconds", 30);
            builder.Services.AddPlatformRequestTimeouts(options => options.AddPolicy(ApiTimeoutPolicy,
                new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(seconds),
                    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
                }));
        }
        builder.Services.AddMsRequestLogging();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        if (builder.Configuration.GetValue("OpenTelemetry:Enabled", true)) builder.Services.AddMsOpenTelemetry(builder.Configuration);
    }

    public static void MapApplication(WebApplication app)
    {
        var rateLimiting = Enabled(app.Configuration, "Http:RateLimiting:Enabled");
        var requestTimeouts = Enabled(app.Configuration, "Http:RequestTimeouts:Enabled");
        app.UsePlatformHttp();
        app.UseMsRequestLogging();
        if (requestTimeouts) app.UsePlatformRequestTimeouts();
        app.UseAuthentication();
        app.UseAuthorization();
        if (rateLimiting) app.UsePlatformRateLimiting();
        app.MapPlatformHealthChecks(WriteReadinessAsync);
        if (rateLimiting || requestTimeouts)
        {
            var api = app.MapGroup("");
            if (rateLimiting) api.RequireRateLimiting(ApiRateLimitPolicy);
            if (requestTimeouts) api.WithRequestTimeout(ApiTimeoutPolicy);
            ProfileEndpoints.Map(api);
        }
        else ProfileEndpoints.Map(app);
    }

    private static bool Enabled(IConfiguration configuration, string key)
    {
        var setting = configuration[key];
        if (setting is null) return false;
        if (bool.TryParse(setting, out var enabled)) return enabled;
        throw new ArgumentException($"{key} must be true or false.");
    }

    private static int PositiveInt(IConfiguration configuration, string key, int fallback)
    {
        var setting = configuration[key];
        if (setting is null) return fallback;
        if (int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            return value;
        throw new ArgumentException($"{key} must be a positive integer.");
    }

    private static async Task WriteReadinessAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("status", report.Status switch
        {
            HealthStatus.Healthy => "healthy",
            HealthStatus.Degraded => "degraded",
            _ => "unhealthy"
        });
        if (report.Status == HealthStatus.Unhealthy)
        {
            var pendingMigrations = report.Entries.TryGetValue("durable-storage", out var storage)
                && storage.Description == "pending_migrations";
            writer.WriteString("reason", pendingMigrations ? "pending_migrations" : "storage_unavailable");
        }
        else writer.WriteString("messaging", context.RequestServices.GetRequiredService<MessagingProviderRegistration>().Name);
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }
}

internal sealed record ReferenceIdempotencyOptions(bool Enabled);

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
