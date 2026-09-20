using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data;
using MS.Microservice.Persistence.SqlSugar.Converters;
using NSubstitute;

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
        var service = CreateService(JsonOptions);
        var obj = new TestDto { Name = "test", Value = 42 };

        var result = service.SerializeObject(obj);

        result.Should().Contain("\"name\"").And.Contain("\"value\"");
    }

    [Fact]
    public void DeserializeObject_ShouldReturnTypedObject()
    {
        var service = CreateService(JsonOptions);
        var json = """{"name":"test","value":42}""";

        var result = service.DeserializeObject<TestDto>(json);

        result.Name.Should().Be("test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void SugarSerializeObject_ShouldUseTheSameConfiguredContractAsSerializeObject()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new JsonStringEnumConverter<CompatibilityState>());
        var service = CreateService(options);
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
        options.Converters.Add(new JsonStringEnumConverter<CompatibilityState>());
        var service = CreateService(options);
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

    private static SqlSugarSerializeService CreateService(JsonSerializerOptions options)
    {
        var context = new PersistenceTestJsonContext(new JsonSerializerOptions(options));
        return new(context.TestDto, context.CompatibilityDto, context.KnownEnvelope, context.String);
    }

    [Fact]
    public void ReflectionIsDisabled_AndMissingRootOrNestedContractFailsExplicitly()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var service = CreateService(new(JsonOptions));
        Assert.Throws<NotSupportedException>(() => service.SerializeObject(new UnregisteredDto()));
        Assert.Throws<NotSupportedException>(() => service.DeserializeObject<UnregisteredDto>("{}"));
        Assert.Throws<NotSupportedException>(() => service.SerializeObject(new KnownEnvelope { Value = new UnregisteredDto() }));
    }

    [Fact]
    public void NullAndMalformedJsonRetainSerializerSemantics()
    {
        var service = CreateService(new(JsonOptions));
        Assert.Equal("null", service.SerializeObject(null!));
        Assert.Null(service.DeserializeObject<TestDto>("null"));
        Assert.Throws<JsonException>(() => service.DeserializeObject<TestDto>("invalid"));
    }

    [Fact]
    public void ColumnConverterRetainsParameterNamingNullDbNullAndQuotedStringSemantics()
    {
        var converter = new ObjectJsonConverter(CreateService(new(JsonOptions)));
        var empty = converter.ParameterConverter<TestDto>(null!, 7);
        Assert.Equal("@7", empty.ParameterName);
        Assert.Null(empty.Value);
        var text = converter.ParameterConverter<string>("plain text", 8);
        Assert.Equal("\"plain text\"", text.Value);
        var record = Substitute.For<IDataRecord>();
        record.GetValue(3).Returns(DBNull.Value);
        Assert.Null(converter.QueryConverter<TestDto>(record, 3));
        record.GetValue(3).Returns(text.Value);
        Assert.Equal("plain text", converter.QueryConverter<string>(record, 3));
    }

    [Fact]
    public void ColumnConverterUsesSuppliedContractForActualRoundTrip()
    {
        var converter = new ObjectJsonConverter(CreateService(new(JsonOptions)));
        var parameter = converter.ParameterConverter<TestDto>(new TestDto { Name = "保存", Value = 42 }, 0);
        var record = Substitute.For<IDataRecord>();
        record.GetValue(0).Returns(parameter.Value);
        var row = converter.QueryConverter<TestDto>(record, 0);
        Assert.Equal("保存", row.Name);
        Assert.Equal(42, row.Value);
        Assert.Throws<NotSupportedException>(() => converter.ParameterConverter<UnregisteredDto>(new UnregisteredDto(), 0));
    }

    public sealed class UnregisteredDto;
    public sealed class KnownEnvelope { public object? Value { get; set; } }

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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SqlSugarSerializeServiceTests.TestDto))]
[JsonSerializable(typeof(SqlSugarSerializeServiceTests.CompatibilityDto))]
[JsonSerializable(typeof(SqlSugarSerializeServiceTests.KnownEnvelope))]
[JsonSerializable(typeof(string))]
internal partial class PersistenceTestJsonContext : JsonSerializerContext;
