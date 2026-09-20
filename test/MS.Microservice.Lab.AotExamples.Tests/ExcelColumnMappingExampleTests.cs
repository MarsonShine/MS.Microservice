using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Excel.ColumnDiscovery;
using MS.Microservice.Lab.AotExamples.Static.Excel;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ExcelColumnMappingExampleTests
{
    private sealed class Row
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void ExplicitColumnsPreserveHeaderOrderAndCurrentValues()
    {
        var row = new Row { Id = 3, Name = "first" };
        var map = new ColumnMapping<Row>(("Id", static r => r.Id), ("Name", static r => r.Name));
        Assert.Equal(LegacyExample.Headers<Row>(), map.Headers());
        Assert.Equal(LegacyExample.Values(row), map.Values(row));
        row.Name = "updated";
        Assert.Equal(LegacyExample.Values(row), map.Values(row));
    }

    [Fact]
    public void ExplicitColumnsCanSelectAndReorderWithoutRuntimeDiscovery()
    {
        var map = new ColumnMapping<Row>(("名称", static r => r.Name));
        Assert.Equal(new[] { "名称" }, map.Headers());
        Assert.Equal(new object?[] { null }, map.Values(new Row { Id = 17 }));
    }
}
