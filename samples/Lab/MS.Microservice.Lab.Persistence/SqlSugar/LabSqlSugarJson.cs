using MS.Microservice.Domain.SqlSugar.Repository;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MS.Microservice.Persistence.SqlSugar;

internal static class LabSqlSugarJson
{
    private static readonly LabSqlSugarJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    public static SqlSugarSerializeService Service { get; } = new(
        Context.UserDemo, Context.ListUserDemo, Context.DictionaryStringObject,
        Context.String, Context.Int32, Context.Int64, Context.Decimal, Context.Double,
        Context.Boolean, Context.DateTime, Context.Guid, Context.ByteArray);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UserDemo))]
[JsonSerializable(typeof(List<UserDemo>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
internal partial class LabSqlSugarJsonContext : JsonSerializerContext;
