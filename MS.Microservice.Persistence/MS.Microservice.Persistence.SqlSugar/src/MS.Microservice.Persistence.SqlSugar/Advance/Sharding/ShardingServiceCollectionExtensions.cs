using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Persistence.SqlSugar.DbContext;
using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace MS.Microservice.Persistence.SqlSugar.Advance.Sharding
{
    public static partial class ShardingServiceCollectionExtensions
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddSqlSugarSharding(Action<ShardingOptions> configure)
            {
                ShardingOptions sqlSugarOptions = new();
                configure(sqlSugarOptions);

                var connectionStrings = sqlSugarOptions.ConnectionStrings;
                if (connectionStrings?.Length > 0)
                {
                    for (int i = 0; i < connectionStrings.Length; i++)
                    {
                        var dbIndex = i;
                        services.AddKeyedScoped<UserSharingDemoDbContext>($"UserRecord{dbIndex}", (sp, obj) =>
                        {
                            var connectionConfig = new ConnectionConfig
                            {
                                ConnectionString = connectionStrings[dbIndex],
                                IsAutoCloseConnection = sqlSugarOptions.IsAutoCloseConnection,
                                DbType = sqlSugarOptions.DbType,
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
                            };
                            UserSharingDemoDbContext client = new(connectionConfig);
                            if (sqlSugarOptions.PrintLog)
                                client.Aop.OnLogExecuting = (sql, pars) =>
                                {
                                    Console.WriteLine(sql + "\r\n" + client.Utilities.SerializeObject(pars.ToDictionary(it => it.ParameterName, it => it.Value)));
                                };
                            client.DbMaintenance.CreateDatabase();
                            client.CodeFirst.InitTables(new[] { typeof(UserDemo) });
                            return client;
                        });
                    }
                }

                return services;
            }
        }
    }
}
