using MS.Microservice.Domain.SqlSugar.Repository;
using System.Text.Json;

namespace MS.Microservice.Persistence.SqlSugar.Tests;

public sealed class LabSqlSugarJsonTests
{
    [Fact]
    public void RegisteredLabTypesAndParameterDictionarySerializeWithGeneratedMetadata()
    {
        var service = LabSqlSugarJson.Service;
        Assert.NotNull(service.DeserializeObject<UserDemo>(service.SerializeObject(new UserDemo())));
        var json = service.SerializeObject(new Dictionary<string, object?>
        {
            ["text"] = "中文", ["count"] = 3, ["id"] = Guid.Empty, ["optional"] = null
        });
        Assert.Contains("中文", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(3, document.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(Guid.Empty, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("optional").ValueKind);
        Assert.Single(service.DeserializeObject<List<UserDemo>>("[{}]"));
    }

    [Fact]
    public void UnknownParameterObjectDoesNotUseReflectionFallback()
    {
        Assert.Throws<NotSupportedException>(() => LabSqlSugarJson.Service.SerializeObject(new Version(1, 2)));
        Assert.Throws<NotSupportedException>(() => LabSqlSugarJson.Service.SerializeObject(
            new Dictionary<string, object?> { ["unknown"] = new Version(1, 2) }));
    }
}
