using System.Text.Json;

namespace MS.Microservice.Persistence.SqlSugar.Tests;

public class SqlSugarSerializeServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void SerializeObject_ShouldReturnJsonString()
    {
        var service = new SqlSugarSerializeService(JsonOptions);
        var obj = new { Name = "test", Value = 42 };

        var result = service.SerializeObject(obj);

        result.Should().Contain("\"name\"").And.Contain("\"value\"");
    }

    [Fact]
    public void DeserializeObject_ShouldReturnTypedObject()
    {
        var service = new SqlSugarSerializeService(JsonOptions);
        var json = """{"name":"test","value":42}""";

        var result = service.DeserializeObject<TestDto>(json);

        result.Name.Should().Be("test");
        result.Value.Should().Be(42);
    }

    public class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}