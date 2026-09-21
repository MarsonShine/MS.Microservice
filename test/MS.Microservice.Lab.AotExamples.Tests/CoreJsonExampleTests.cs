using System.Text.Json.Serialization;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.CoreJson.JsonRoundTrip;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.CoreJson.JsonRoundTrip;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed partial class CoreJsonExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("中文 <&>")]
    public void MetadataOverloadsPreserveValues(string? name)
    {
        var value = new Item(name, new Child(7));
        Assert.Equal(value, LegacyExample.Copy(value));
        Assert.Equal(value, StaticExample.Copy(value, ExampleJson.Default.Item));
    }

    public sealed record Item(string? Name, Child Child);
    public sealed record Child(int Value);
    [JsonSerializable(typeof(Item))]
    private partial class ExampleJson : JsonSerializerContext;
}
