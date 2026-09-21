using MS.Microservice.Lab.AotExamples.Legacy.Excel.ManualMapping;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ExcelTemplateMappingExampleTests
{
    [Fact]
    public void ExplicitTemplateMappingMatchesSparseHeadersAndSkipsAbsentColumns()
    {
        var row = (Id: 7, Name: "sample");
        var result = TemplateMapping.BindRow(row, new Dictionary<string, int> { ["Name"] = 5 },
            ("Id", static r => r.Id), ("Name", static r => r.Name));
        Assert.Equal((5, (object?)"sample"), Assert.Single(result));
    }
}
