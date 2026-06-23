using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar.DbContext
{
    public class UserDemoDbContext(ConnectionConfig config) : SqlSugarScope(config)
    {
    }
}
