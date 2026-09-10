using MS.Microservice.Persistence.SqlSugar;
using SqlSugar;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqlSugarPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddSqlSugarClient<TClient>(this IServiceCollection services,
        SqlSugarClientBuilderOptions options, Func<ConnectionConfig> connectionConfigFunc,
        Func<ConnectionConfig, TClient> clientBuilder) where TClient : class, ISqlSugarClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionConfigFunc);
        ArgumentNullException.ThrowIfNull(clientBuilder);
        services.AddScoped<TClient>(_ =>
        {
            var configuration = connectionConfigFunc() ?? throw new InvalidOperationException("The connection factory returned null.");
            var client = clientBuilder(configuration) ?? throw new InvalidOperationException("The client factory returned null.");
            if (options.PrintLog)
                client.Aop.OnLogExecuting = (sql, parameters) =>
                    Console.WriteLine($"SqlSugar command; parameters={parameters.Length}");
            return client;
        });
        return services;
    }
}
