namespace Dapper
{
    public static class SqlBuilderExtensions
    {
        public static SqlBuilder WhereIf(this SqlBuilder builder, bool condition, string sql, object parameters = null)
        {
            if (condition)
            {
                builder.Where(sql, parameters);
            }
            return builder;
        }
    }
}
