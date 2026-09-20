using SqlSugar;
using System.Data;

namespace MS.Microservice.Persistence.SqlSugar.Converters;

/// <summary>For attribute-based ORM activation, derive a parameterless converter that supplies its registered service.</summary>
public class ObjectJsonConverter(SqlSugarSerializeService serializeService) : ISugarDataConverter
{
    private readonly SqlSugarSerializeService serializeService = serializeService
        ?? throw new ArgumentNullException(nameof(serializeService));

    public SugarParameter ParameterConverter<T>(object columnValue, int columnIndex) =>
        new("@" + columnIndex, columnValue is null ? null : serializeService.SerializeObject(columnValue));

    public T QueryConverter<T>(IDataRecord dataRecord, int dataRecordIndex)
    {
        var value = dataRecord.GetValue(dataRecordIndex);
        return value == DBNull.Value ? default! : serializeService.DeserializeObject<T>(value.ToString()!);
    }
}
