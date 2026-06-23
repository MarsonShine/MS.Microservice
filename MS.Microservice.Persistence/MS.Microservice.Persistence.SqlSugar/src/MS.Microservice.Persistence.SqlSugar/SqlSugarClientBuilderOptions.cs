using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar
{
    public class SqlSugarClientBuilderOptions
    {
        public string ConnectionString { get; set; } = string.Empty;

        public DbType DbType { get; set; }

        public bool IsAutoCloseConnection { get; set; }

        public bool PrintLog { get; set; }
    }
}
