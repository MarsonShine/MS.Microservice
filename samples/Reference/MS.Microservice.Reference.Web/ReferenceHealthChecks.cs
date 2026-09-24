using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal sealed class ReferenceDurableStorageHealthCheck(
    ReferenceDbContext database, IMessageStorageProbe storage, ReferenceIdempotencyOptions idempotency) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await database.Profiles.AsNoTracking().AnyAsync(cancellationToken);
        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken))
            .Any(migration => idempotency.Enabled || !IsIdempotencyMigration(migration)))
            return HealthCheckResult.Unhealthy("pending_migrations");
        await storage.CheckAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }

    private static bool IsIdempotencyMigration(string migration)
        => migration is "20260923112723_AddHttpIdempotency" or "20260923112738_AddHttpIdempotency";
}

internal sealed class ReferenceBrokerHealthCheck(RabbitMqTransport broker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await broker.ProbeAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded();
}
