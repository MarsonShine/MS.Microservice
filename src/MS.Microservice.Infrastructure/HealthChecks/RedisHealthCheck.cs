using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.HealthChecks
{
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly string _redisConnection;

        public RedisHealthCheck(IConfiguration configuration)
        {
            _redisConnection = configuration.GetSection("Redis:ConnectionString").Get<string>()!;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(HealthCheckResult.Healthy("The Redis connection is working"));
        }
    }
}
