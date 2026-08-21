using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace MS.Microservice.Infrastructure.HealthChecks;

public sealed class SqlHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public const string Name = "postgresql";
    public const string ReadinessTag = "ready";
    private readonly string _connectionString =
        configuration.GetConnectionString("ActivationConnection") ?? string.Empty;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Unhealthy("PostgreSQL database configuration is missing.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL database is unavailable.",
                exception: exception);
        }
    }
}
