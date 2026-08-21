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

    [Fact]
    public void SugarSerializeObject_ShouldUseTheSameConfiguredContractAsSerializeObject()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var service = new SqlSugarSerializeService(options);
        var value = new CompatibilityDto
        {
            DisplayName = "compatible",
            State = CompatibilityState.Ready
        };

        var sugarJson = service.SugarSerializeObject(value);
        var regularJson = service.SerializeObject(value);

        sugarJson.Should().Be(regularJson);
        sugarJson.Should().Contain("\"displayName\"")
            .And.Contain("\"Ready\"");
    }

    [Fact]
    public void SugarSerializeObject_Output_ShouldRoundTripThroughConfiguredDeserializer()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var service = new SqlSugarSerializeService(options);
        var value = new CompatibilityDto
        {
            DisplayName = "round-trip",
            State = CompatibilityState.Ready
        };

        var json = service.SugarSerializeObject(value);
        var result = service.DeserializeObject<CompatibilityDto>(json);

        result.DisplayName.Should().Be(value.DisplayName);
        result.State.Should().Be(value.State);
    }

    public class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class CompatibilityDto
    {
        public string DisplayName { get; set; } = string.Empty;
        public CompatibilityState State { get; set; }
    }

    public enum CompatibilityState
    {
        Ready
    }
}
