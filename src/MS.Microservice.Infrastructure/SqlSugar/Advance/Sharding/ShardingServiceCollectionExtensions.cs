using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Infrastructure.DbContext.SqlSugar;
using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
	public static class ShardingServiceCollectionExtensions
	{
		public static IServiceCollection AddSqlSugarSharding(this IServiceCollection services, Action<ShardingOptions> configure)
		{
			ShardingOptions sqlSugarOptions = new();
			configure(sqlSugarOptions);

			var connectionStrings = sqlSugarOptions.ConnectionStrings;
			if (connectionStrings?.Length > 0)
			{
				for (int i = 0; i < connectionStrings.Length; i++)
				{
					var connectionConfig = new ConnectionConfig
					{
						ConnectionString = connectionStrings[i],
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
								// 这里配置实体
							},
							SerializeService = new SqlSugarSerializeService(DefaultSerializeSetting.Default),
						},
					};
					UserSharingDemoDbContext client = new(connectionConfig);
					// 全局过滤器
					//client.QueryFilter.AddTableFilter<Word>(d => d.DeletedAt == null);
					if (sqlSugarOptions.PrintLog)
						client.Aop.OnLogExecuting = (sql, pars) =>
						{
							Console.WriteLine(sql + "\r\n" + client.Utilities.SerializeObject(pars.ToDictionary(it => it.ParameterName, it => it.Value)));
						};
					client.DbMaintenance.CreateDatabase();
					client.CodeFirst.InitTables(new[] { typeof(UserDemo) });
					services.AddKeyedScoped<UserSharingDemoDbContext>($"UserRecord{i}", (sp, obj) => client);
				}
			}
			return services;
		}
	}
}
