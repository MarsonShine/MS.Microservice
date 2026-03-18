namespace Dapper
{
    public static partial class SqlBuilderExtensions
    {
        extension(SqlBuilder builder)
        {
            public SqlBuilder WhereIf(bool condition, string sql, object? parameters = null)
            {
                if (condition)
                {
                    builder.Where(sql, parameters);
                }
                return builder;
            }
        }
    }
}
