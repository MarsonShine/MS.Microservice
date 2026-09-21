using MS.Microservice.Excel.Aot;
using MS.Microservice.Lab.AotExamples.Static.Excel;
using NPOI.XSSF.UserModel;
using Xunit;
using Old = MS.Microservice.Lab.AotExamples.Legacy.Excel.ManualMapping;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ExcelGeneratedMappingExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("中文 <&>")]
    public void GeneratedMappingPreservesValuesWithNoHandwrittenPropertyDelegates(string? name)
    {
        var input = new GeneratedBook { Id = 7, Name = name };
        var old = new Old.ExcelModelMap<GeneratedBook>(static () => new(),
            Old.ExcelColumn<GeneratedBook>.Create("编号", static b => b.Id, static (b, v) => b.Id = v, Old.ExcelValueConverters.Int32),
            Old.ExcelColumn<GeneratedBook>.Create("名称", static b => b.Name, static (b, v) => b.Name = v, Old.ExcelValueConverters.String));
        var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
        var bytes = helper.Export([input], "Books", GeneratedBooks.GeneratedBook);
        using var workbook = new XSSFWorkbook(new MemoryStream(bytes));
        var sheet = workbook.GetSheetAt(0);
        Assert.Equal(new[] { "编号", "名称" }, sheet.GetRow(0).Cells.Select(c => c.StringCellValue));
        Assert.Equal(old.Slots[0].Getter(input), (int)sheet.GetRow(1).GetCell(0).NumericCellValue);
        Assert.Equal(old.Slots[1].Getter(input), sheet.GetRow(1).GetCell(1)?.StringCellValue);
        try
        {
            var decoded = Assert.Single(helper.Import(bytes, GeneratedBooks.GeneratedBook));
            Assert.Equal(7, decoded.Id);
            Assert.Equal(string.IsNullOrWhiteSpace(name) ? null : name, decoded.Name);
        }
        finally { helper.Workbook?.Dispose(); }
    }
}
