using SqlSugar;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
    public interface IUserSpecificSqlSugarClientProvider
    {
        ISqlSugarClient Client { get; }
    }
}
