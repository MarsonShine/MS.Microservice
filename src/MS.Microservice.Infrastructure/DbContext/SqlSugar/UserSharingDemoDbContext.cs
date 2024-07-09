using SqlSugar;

namespace MS.Microservice.Infrastructure.DbContext.SqlSugar
{
	public class UserSharingDemoDbContext(ConnectionConfig config) : SqlSugarScope(config)
	{
	}
}
