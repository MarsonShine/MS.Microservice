using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Infrastructure.SqlSugar;
using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Microsoft.Extension.DependencyInjection
{
    public static class SqlSugarServiceCollectionExtensions
    {
        public static void AddSqlSugarService(this IServiceCollection services, IConfiguration configuration)
        {
            var sqlSugarOptions = configuration.GetSection("SqlSugarOptions").Get<SqlSugarOptions>() ?? new SqlSugarOptions
            {
                IsAutoCloseConnection = true,
                PrintLog = false,
            };
            ConnectionConfig dbConfig = new()
            {
                ConnectionString = configuration.GetConnectionString("Default"),
                IsAutoCloseConnection = sqlSugarOptions.IsAutoCloseConnection,
                DbType = DbType.MySql,
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
                        UserEntityConfiguration(entity);
                    }
                },
            };
            SqlSugarScope sqlSugarScope = new(dbConfig, client =>
            {
                // 全局过滤器
                //client.QueryFilter.AddTableFilter<User>(d => d.DeletedAt == null);
                if (sqlSugarOptions.PrintLog)
                    client.Aop.OnLogExecuting = (sql, pars) =>
                    {
                        Console.WriteLine(sql + "\r\n" + client.Utilities.SerializeObject(pars.ToDictionary(it => it.ParameterName, it => it.Value)));
                    };
            });

            services.AddScoped<ISqlSugarClient>(sp => sqlSugarScope);

            sqlSugarScope.CodeFirst.InitTables(new Type[] { typeof(User) });
        }

        private static void UserEntityConfiguration(EntityColumnInfo entity)
        {
            // 具体实体配置详见：
            // 一对多
            //entity.IfTable<Table1>()
            //    .OneToMany(p => p.RoundQuestionBanks, nameof(Table2.UserQuestionBankRoundId), nameof(Table1.Id))
            //    .UpdateProperty(p => p.Id, c =>
            //    {
            //        c.IsPrimarykey = true;
            //        c.IsIdentity = false;
            //    })
            //    ;
            // 一对一
            //entity.IfTable<Table1>()
            //    .OneToOne(p => p.Table2, nameof(Table1.QuestionBankId), nameof(Table2.Id))
            //    ;
        }
    }
}
