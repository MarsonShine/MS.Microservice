using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extension.DependencyInjection
{
    public static class SqlSugarServiceCollectionCompatibilityExtensions
    {
        public static IServiceCollection AddSqlSugarService(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services.AddMicroserviceSqlSugarPersistence(configuration);
        }
    }
}
