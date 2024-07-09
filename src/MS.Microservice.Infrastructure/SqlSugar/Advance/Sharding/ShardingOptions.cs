using SqlSugar;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
	public class ShardingOptions
	{
		public string[]? ConnectionStrings { get; set; }
		public DbType DbType { get; set; }
		public bool PrintLog { get; set; }
		public bool IsAutoCloseConnection { get; set; }
		public int Count => ConnectionStrings?.Length > 0 ? ConnectionStrings!.Length : 0;
	}
}
