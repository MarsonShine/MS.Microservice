using Dapper;

namespace MS.Microservice.Web.Infrastructure.Dapper
{
    public class ExtendedSqlBuilder : SqlBuilder
    {
        public SqlBuilder PageBy(string sql, dynamic parameters = null) => AddClause("pageby", sql, parameters, "", "\n LIMIT ", "\n", false);
    }
}
