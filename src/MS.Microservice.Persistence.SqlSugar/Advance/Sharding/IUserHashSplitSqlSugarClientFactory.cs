using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar.Advance.Sharding
{
    public interface IUserHashSplitSqlSugarClientFactory
    {
        ISqlSugarClient GetSqlSugarClient(long userId);
    }
}
