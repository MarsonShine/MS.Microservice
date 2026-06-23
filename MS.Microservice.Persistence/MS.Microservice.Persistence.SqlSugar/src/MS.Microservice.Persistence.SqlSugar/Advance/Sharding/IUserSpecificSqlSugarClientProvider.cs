using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar.Advance.Sharding
{
    public interface IUserSpecificSqlSugarClientProvider
    {
        ISqlSugarClient Client { get; }
    }
}
