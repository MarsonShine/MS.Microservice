using MS.Microservice.Core.Serialization;
using SqlSugar;
using System;
using System.Data;

namespace MS.Microservice.Infrastructure.SqlSugar.Converters
{
	public class ObjectJsonConverter : ISugarDataConverter
	{
		private static readonly SqlSugarSerializeService serializeService = new SqlSugarSerializeService(DefaultSerializeSetting.Default);
		public SugarParameter ParameterConverter<T>(object columnValue, int columnIndex)
		{
			string value;
			if (columnValue == null)
				return new SugarParameter("@" + columnIndex, null);

			if (columnValue.GetType() == typeof(string))
			{
				value = columnValue.ToString()!;
			}
			value = serializeService.SerializeObject(columnValue);
			return new SugarParameter("@" + columnIndex, value);
		}

		public T QueryConverter<T>(IDataRecord dataRecord, int dataRecordIndex)
		{
			var value = dataRecord.GetValue(dataRecordIndex);
			if (value == DBNull.Value)
				return default!;
			return serializeService.DeserializeObject<T>(value.ToString()!);
		}
	}
}
