using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Infrastructure.DbContext.SqlSugar;
using MS.Microservice.Infrastructure.SqlSugar;
using MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding;
using MS.Microservice.Infrastructure.SqlSugar.Converters;
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
				ConnectionString = configuration.GetConnectionString("Default"),
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
						//// 这里添加实体配置
						//// Json 实体配置，自定义转换
						//entity.IsJson = true;
						//entity.SqlParameterDbType = typeof(ObjectJsonConverter);
					},
					SerializeService = new SqlSugarSerializeService(DefaultSerializeSetting.Default),
				},
			}, (dbConfig) => new UserDemoDbContext(dbConfig));
			// sharding
			var shardingOptions = configuration.GetSection("ShardingOptions").Get<ShardingOptions>()
				?? throw new ArgumentException(nameof(ShardingOptions));
			services.AddSqlSugarSharding((opt) =>
			{
				opt.ConnectionStrings = shardingOptions.ConnectionStrings;
				opt.DbType = shardingOptions.DbType;
				opt.IsAutoCloseConnection = shardingOptions.IsAutoCloseConnection;
				opt.PrintLog = shardingOptions.PrintLog;
			});
		}

		public static IServiceCollection AddSqlSugarClient<TSqlSugarClient>(this IServiceCollection services,
			SqlSugarClientBuilderOptions options,
			Func<ConnectionConfig> connectionConfigFunc, Func<ConnectionConfig, TSqlSugarClient> clientBuilder)
			where TSqlSugarClient : class, ISqlSugarClient
		{
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

		private static void AddSqlSugarServiceCore(IServiceCollection services, IConfiguration configuration)
		{
			services.Configure<SqlSugarOptions>(configuration);
			services.Configure<ShardingOptions>(configuration);
			services.AddTransient<IUserHashSplitSqlSugarClientFactory, UserHashSplitSqlSugarClientFactory>();
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

		public static void AddSqlSugarRepository(this IServiceCollection services)
		{
			// inject repository
		}
	}
}
