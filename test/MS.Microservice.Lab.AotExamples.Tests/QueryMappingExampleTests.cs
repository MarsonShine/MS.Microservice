using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Query;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.Query;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class QueryMappingExampleTests
{
    [Fact]
    public void StaticDefinitionMatchesCachedReflectionAndReadsFreshValues()
    {
        var map = new StaticExample.QueryMappingExample<Model>(
            ("Name", static value => value.Name), ("Ids", static value => value.Ids));
        var value = new Model { Name = "中文 &", Ids = [1, 2] };
        Assert.Equal(LegacyExample.QueryMappingExample<Model>.Format(value), map.Format(value));
        Assert.Equal("Name=%E4%B8%AD%E6%96%87%20%26&Ids=1&Ids=2", map.Format(value));
        value.Name = null;
        value.Ids = [];
        Assert.Equal("", map.Format(value));
        Assert.Equal(LegacyExample.QueryMappingExample<Model>.Format(value), map.Format(value));
    }

    public sealed class Model
    {
        public string? Name { get; set; }
        public int[]? Ids { get; set; }
    }
}

