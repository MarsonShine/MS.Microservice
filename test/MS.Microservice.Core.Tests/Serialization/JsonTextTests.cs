using System.Text.Json;
using MS.Microservice.Core.Serialization;

namespace MS.Microservice.Core.Tests.Serialization;

public sealed class JsonTextTests
{
    [Fact]
    public void Format_PreservesPropertyOrderDuplicatesNamesAndNumericLexemes()
    {
        const string json = "{\"PascalName\":123456789012345678901234567890,\"number\":1.2300e+04,\"number\":-0,\"nested\":[true,null,\"中文🙂\"]}";

        var formatted = JsonText.Format(json);
        using var document = JsonDocument.Parse(formatted);
        var properties = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(new[] { "PascalName", "number", "number", "nested" }, properties.Select(property => property.Name));
        Assert.Equal("123456789012345678901234567890", properties[0].Value.GetRawText());
        Assert.Equal("1.2300e+04", properties[1].Value.GetRawText());
        Assert.Equal("-0", properties[2].Value.GetRawText());
        Assert.Equal("中文🙂", properties[3].Value[2].GetString());
        Assert.Contains('\n', formatted);
    }

    [Theory]
    [InlineData("  { \"A\" : [ 1, true, null, \"a b\" ] }  ", "{\"A\":[1,true,null,\"a b\"]}")]
    [InlineData(" [ 1e400, 1.00, -0.0 ] ", "[1e400,1.00,-0.0]")]
    [InlineData(" true ", "true")]
    [InlineData(" null ", "null")]
    [InlineData(" 42 ", "42")]
    [InlineData(" \"text\" ", "\"text\"")]
    public void Format_CompactModePreservesOneJsonValue(string input, string expected)
    {
        Assert.Equal(expected, JsonText.Format(input, indented: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{bad}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("/* comment */ {}")]
    [InlineData("{} []")]
    [InlineData("NaN")]
    public void Format_RejectsInvalidOrMultipleJsonValues(string input)
    {
        Assert.ThrowsAny<JsonException>(() => JsonText.Format(input));
    }

    [Fact]
    public void Format_DoesNotUseApplicationConvertersOrNamingPolicy()
    {
        const string input = "{\"Phone\":\"13812345678\",\"Long\":9223372036854775807,\"Date\":\"2026-10-10T00:00:00+08:00\"}";

        using var result = JsonDocument.Parse(JsonText.Format(input, indented: false));
        var properties = result.RootElement.EnumerateObject().ToArray();
        Assert.Equal(new[] { "Phone", "Long", "Date" }, properties.Select(property => property.Name));
        Assert.Equal("13812345678", properties[0].Value.GetString());
        Assert.Equal(JsonValueKind.Number, properties[1].Value.ValueKind);
        Assert.Equal("9223372036854775807", properties[1].Value.GetRawText());
        Assert.Equal("2026-10-10T00:00:00+08:00", properties[2].Value.GetString());
        Assert.Throws<ArgumentNullException>(() => JsonText.Format(null!));
    }

    [Fact]
    public void Format_DecodedStringsRemainIdenticalWhenEscapingChanges()
    {
        const string json = "{\"\\u4e2d\\u6587\":\"\\u4e2d\\u6587\\n\\\"\\\\\\t🙂\"}";
        using var original = JsonDocument.Parse(json);
        using var result = JsonDocument.Parse(JsonText.Format(json));

        Assert.Equal(original.RootElement.EnumerateObject().Single().Name,
            result.RootElement.EnumerateObject().Single().Name);
        Assert.Equal(original.RootElement.GetProperty("中文").GetString(), result.RootElement.GetProperty("中文").GetString());
    }
}
