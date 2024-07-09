using SqlSugar;

namespace MS.Microservice.Infrastructure.DbContext.SqlSugar
{
	public class UserDemoDbContext(ConnectionConfig config) : SqlSugarScope(config)
	{
	}
}
