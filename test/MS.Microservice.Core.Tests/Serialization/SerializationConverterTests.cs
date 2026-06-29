using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MS.Microservice.Core.Serialization.Converters;
using Xunit;

namespace MS.Microservice.Core.Tests.Serialization;

public sealed class SerializationConverterTests
{
    [Fact]
    public void DateTimeOffsetConverter_ShouldReadAndWrite()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new DateTimeOffsetConverter());

        var holder = JsonSerializer.Deserialize<DateTimeOffsetHolder>("{\"Value\":\"2024-01-02 03:04:05\"}", options)!;
        holder.Value.Should().Be(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)));

        string json = JsonSerializer.Serialize(new DateTimeOffsetHolder
        {
            Value = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(8))
        }, options);

        json.Should().Contain("2024-01-02 03:04:05");
    }

    [Fact]
    public void StringDateTimeOffsetConverter_ShouldReadAndWrite()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StringDateTimeOffsetConverter());

        var holder = JsonSerializer.Deserialize<StringDateTimeHolder>("{\"Value\":\"2024-01-02 03:04:05\"}", options)!;
        holder.Value.Should().Be("2024-01-02 03:04:05");

        string json = JsonSerializer.Serialize(new StringDateTimeHolder { Value = "" }, options);
        json.Should().Contain("0001-01-01");
    }

    [Fact]
    public void PhoneDesensitizationConverter_ShouldMaskPhoneNumbers()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PhoneDesensitizationConverter());

        string masked = JsonSerializer.Serialize(new PhoneHolder { Value = "13812345678" }, options);
        string shortValue = JsonSerializer.Serialize(new PhoneHolder { Value = "12345" }, options);

        masked.Should().Contain("138****5678");
        shortValue.Should().Contain("12345");
    }

    [Fact]
    public void StringLongConverter_ShouldReadAndWrite()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StringLongConverter());

        JsonSerializer.Deserialize<LongHolder>("{\"Value\":\"42\"}", options)!.Value.Should().Be(42);
        JsonSerializer.Deserialize<LongHolder>("{\"Value\":43}", options)!.Value.Should().Be(43);

        string json = JsonSerializer.Serialize(new LongHolder { Value = 44 }, options);
        json.Should().Contain("\"44\"");
    }

    [Fact]
    public void StringLongConverter_ShouldThrow_WhenValueIsInvalid()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StringLongConverter());

        Action action = () => JsonSerializer.Deserialize<LongHolder>("{\"Value\":true}", options);

        action.Should().Throw<JsonException>();
    }

    private sealed class DateTimeOffsetHolder
    {
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset Value { get; set; }
    }

    private sealed class StringDateTimeHolder
    {
        [JsonConverter(typeof(StringDateTimeOffsetConverter))]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class PhoneHolder
    {
        [JsonConverter(typeof(PhoneDesensitizationConverter))]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class LongHolder
    {
        [JsonConverter(typeof(StringLongConverter))]
        public long Value { get; set; }
    }
}
