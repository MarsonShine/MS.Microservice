using SqlSugar;

namespace MS.Microservice.Infrastructure.SqlSugar
{
	public class SqlSugarClientBuilderOptions
	{
		public string ConnectionString { get; set; } = "";
		public DbType DbType { get; set; }
		public bool IsAutoCloseConnection { get; set; }
		public bool PrintLog { get; set; }
	}
}