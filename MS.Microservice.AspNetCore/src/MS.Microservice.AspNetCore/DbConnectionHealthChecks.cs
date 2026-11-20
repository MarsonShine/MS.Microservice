using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MS.Microservice.AspNetCore;

public static class DbConnectionHealthChecks
{
    public static IHealthChecksBuilder AddDbConnectionCheck(this IHealthChecksBuilder builder,
        string name, Func<IServiceProvider, DbConnection> connectionFactory,
        string testQuery = "SELECT 1", TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(testQuery);

        return builder.Add(new HealthCheckRegistration(name,
            services => new DbConnectionHealthCheck(() => connectionFactory(services), testQuery),
            HealthStatus.Unhealthy, [PlatformHealthChecks.ReadyTag], timeout ?? TimeSpan.FromSeconds(5)));
    }

    private sealed class DbConnectionHealthCheck(Func<DbConnection> createConnection, string testQuery) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = createConnection();
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = testQuery;
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy("Database is unavailable.", exception);
            }
        }
    }
}
