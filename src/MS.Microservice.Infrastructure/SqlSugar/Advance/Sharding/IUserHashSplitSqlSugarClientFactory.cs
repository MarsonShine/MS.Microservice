using SqlSugar;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
	public interface IUserHashSplitSqlSugarClientFactory
	{
		ISqlSugarClient GetSqlSugarClient(long userId);
	}
}
