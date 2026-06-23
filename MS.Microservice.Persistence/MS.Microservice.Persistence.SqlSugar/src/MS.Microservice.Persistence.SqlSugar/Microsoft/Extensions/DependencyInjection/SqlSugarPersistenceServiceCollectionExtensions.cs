using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Persistence.SqlSugar;
using MS.Microservice.Persistence.SqlSugar.Advance.Sharding;
using MS.Microservice.Persistence.SqlSugar.DbContext;
using MS.Microservice.Persistence.SqlSugar.Repository;
using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SqlSugarPersistenceServiceCollectionExtensions
    {
        public static IServiceCollection AddMicroserviceSqlSugarPersistence(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddSqlSugarServiceCore(services, configuration);

            var sqlSugarOptions = configuration.GetSection("SqlSugarOptions").Get<SqlSugarOptions>() ?? new SqlSugarOptions
            {
                IsAutoCloseConnection = true,
                PrintLog = false,
            };

            services.AddSqlSugarClient<UserDemoDbContext>(new()
            {
                PrintLog = sqlSugarOptions.PrintLog,
            },
            () => new()
            {
                ConnectionString = configuration.GetConnectionString("Default") ?? string.Empty,
                IsAutoCloseConnection = sqlSugarOptions.IsAutoCloseConnection,
                DbType = DbType.PostgreSQL,
                MoreSettings = new()
                {
                    PgSqlIsAutoToLower = false,
                    PgSqlIsAutoToLowerCodeFirst = false,
                },
                ConfigureExternalServices = new ConfigureExternalServices()
                {
                    EntityNameService = (type, entity) =>
                    {
                        var tableAttribute = type.GetCustomAttributes(false)
                            .Where(p => p.GetType() == typeof(TableAttribute))
                            .Cast<TableAttribute>()
                            .FirstOrDefault();
                        if (tableAttribute != null)
                            entity.DbTableName = tableAttribute.Name;
                    },
                    EntityService = (type, entity) =>
                    {
                        if (entity.PropertyName == "Id")
                        {
                            entity.IsPrimarykey = true;
                            entity.IsIdentity = true;
                        }
                    },
                    SerializeService = new SqlSugarSerializeService(DefaultSerializeSetting.Default),
                },
            }, dbConfig => new UserDemoDbContext(dbConfig));

            var shardingOptions = configuration.GetSection("ShardingOptions").Get<ShardingOptions>()
                ?? throw new ArgumentException(nameof(ShardingOptions));
            services.AddSqlSugarSharding(opt =>
            {
                opt.ConnectionStrings = shardingOptions.ConnectionStrings;
                opt.DbType = shardingOptions.DbType;
                opt.IsAutoCloseConnection = shardingOptions.IsAutoCloseConnection;
                opt.PrintLog = shardingOptions.PrintLog;
            });

            services.AddSqlSugarRepository();
            return services;
        }

        public static IServiceCollection AddSqlSugarClient<TSqlSugarClient>(
            this IServiceCollection services,
            SqlSugarClientBuilderOptions options,
            Func<ConnectionConfig> connectionConfigFunc,
            Func<ConnectionConfig, TSqlSugarClient> clientBuilder)
            where TSqlSugarClient : class, ISqlSugarClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(connectionConfigFunc);
            ArgumentNullException.ThrowIfNull(clientBuilder);

            ConnectionConfig dbConfig = connectionConfigFunc()
                ?? throw new ArgumentNullException(nameof(connectionConfigFunc));

            var client = clientBuilder(dbConfig);

            if (options.PrintLog)
                client.Aop.OnLogExecuting = (sql, pars) =>
                {
                    Console.WriteLine(sql + "\r\n" + client.Utilities.SerializeObject(pars.ToDictionary(it => it.ParameterName, it => it.Value)));
                };

            services.AddScoped<TSqlSugarClient>(sp => client);
            return services;
        }

        public static IServiceCollection AddSqlSugarRepository(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddScoped<IUserDemoRepository, UserDemoRepository>();
            return services;
        }

        private static void AddSqlSugarServiceCore(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SqlSugarOptions>(configuration.GetSection("SqlSugarOptions"));
            services.Configure<ShardingOptions>(configuration.GetSection("ShardingOptions"));
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<IUserHashSplitSqlSugarClientFactory, UserHashSplitSqlSugarClientFactory>();
            services.AddTransient<IUserSpecificSqlSugarClientProvider, UserSpecificSqlSugarClientProvider>();
        }
    }
}
