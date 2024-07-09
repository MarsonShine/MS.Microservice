using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Infrastructure.DbContext.SqlSugar;
using SqlSugar;
using System;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
	public class UserHashSplitSqlSugarClientFactory(IServiceProvider serviceProvider, IOptions<ShardingOptions> options) : IUserHashSplitSqlSugarClientFactory
	{
		public ISqlSugarClient GetSqlSugarClient(long userId)
		{
			int shardId = (int)(userId % options.Value.Count);
			const string shardKey = "UserRecord";
			return serviceProvider.GetRequiredKeyedService<UserSharingDemoDbContext>($"{shardKey}{shardId}");
		}

	}
}
