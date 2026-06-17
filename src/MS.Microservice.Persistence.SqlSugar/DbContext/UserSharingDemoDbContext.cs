using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar.DbContext
{
    public class UserSharingDemoDbContext(ConnectionConfig config) : SqlSugarScope(config)
    {
    }
}
