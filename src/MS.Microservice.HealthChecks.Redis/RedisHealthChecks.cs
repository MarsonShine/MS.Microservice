using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MS.Microservice.HealthChecks.Redis;

public static class RedisHealthChecks
{
    public const string DefaultName = "redis";
    public const string ReadinessTag = "ready";

    public static IHealthChecksBuilder AddRedisHealthCheck(this IHealthChecksBuilder builder,
        string name = DefaultName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.Add(new HealthCheckRegistration(name,
            static services => new RedisHealthCheck(services),
            HealthStatus.Unhealthy, [ReadinessTag], TimeSpan.FromSeconds(5)));
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly Func<IConnectionMultiplexer> _getConnection;

    public RedisHealthCheck(IConnectionMultiplexer connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _getConnection = () => connection;
    }

    internal RedisHealthCheck(IServiceProvider services)
    {
        _getConnection = () => services.GetRequiredService<IConnectionMultiplexer>();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _getConnection().GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Redis PING failed.");
        }
    }
}
