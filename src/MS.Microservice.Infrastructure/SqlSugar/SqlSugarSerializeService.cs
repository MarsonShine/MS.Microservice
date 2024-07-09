using SqlSugar;
using System;
using System.Text.Json;

namespace MS.Microservice.Infrastructure.SqlSugar
{
	/// <summary>
	/// 替换默认SqlSugar序列化服务
	/// </summary>
	/// <param name="options"></param>
	public class SqlSugarSerializeService(JsonSerializerOptions options) : ISerializeService
	{
		public T DeserializeObject<T>(string value)
		{
			return JsonSerializer.Deserialize<T>(value, options)!;
		}

		public string SerializeObject(object value)
		{
			return JsonSerializer.Serialize(value, options);
		}

		public string SugarSerializeObject(object value)
		{
			throw new NotImplementedException();
		}
	}
}
