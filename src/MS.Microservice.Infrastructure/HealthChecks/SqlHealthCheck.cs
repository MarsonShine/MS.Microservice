using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.HealthChecks
{
    public class SqlHealthCheck : IHealthCheck
    {
        public const string Name = nameof(SqlHealthCheck);
        private readonly string _connectionString;
        public SqlHealthCheck(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Default")!;
        }
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var sqlConnection = new MySqlConnection(_connectionString);
                await sqlConnection.OpenAsync(cancellationToken);
                using var command = sqlConnection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("数据库连接异常", exception: ex);
            }
        }
    }
}
