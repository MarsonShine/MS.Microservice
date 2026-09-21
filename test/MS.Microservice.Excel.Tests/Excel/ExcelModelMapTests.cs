using MS.Microservice.Excel.Aot;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MS.Microservice.Excel.Tests.Excel;

public sealed partial class ExcelModelMapTests
{
    private enum Status { None, Ready }
    private enum ByteStatus : byte { Maximum = 255 }
    private enum ShortStatus : short { Negative = -2 }
    private enum LongStatus : long { Large = 2147483648 }

    private sealed class EnumRow
    {
        public ByteStatus Byte { get; set; }
        public ShortStatus Short { get; set; }
        public LongStatus Long { get; set; }
        public Status? Optional { get; set; }
    }

    private sealed class Row(string seed)
    {
        [ExcelColumn(Order = 6)]
        public string ReadOnly => seed;
        [ExcelColumn(Order = 1)]
        public int Number { get; set; } = 41;
        [ExcelColumn(Order = 2)]
        public int? Optional { get; set; }
        [ExcelColumn(Order = 3)]
        public DateTime Date { get; set; }
        [ExcelColumn(Order = 4)]
        public Guid Id { get; set; }
        [ExcelColumn(Order = 5)]
        public Status Status { get; set; }
        [ExcelColumn(Converter = typeof(UppercaseConverter), Order = 0)]
        public string? Label { get; set; }
        [ExcelColumn(Ignore = true)]
        public string Undeclared => throw new InvalidOperationException("Must not discover this property.");
    }

    private static ExcelModelMap<Row> Map => Maps.Row;
    private sealed class UppercaseConverter : IExcelCellConverter<string?>
    {
        static bool IExcelCellConverter<string?>.TryRead(ExcelCellReader reader, out string? value)
        {
            if (!reader.TryReadString(out var text)) { value = null; return false; }
            value = text.ToUpperInvariant();
            return true;
        }
        static void IExcelCellConverter<string?>.Write(ICell cell, string? value, ICellStyle? dateStyle) => cell.SetCellValue(value ?? "");
    }

    [Fact]
    public void Export_UsesDeclaredOrderAndReadsCurrentValues_WithoutDiscoveringOtherProperties()
    {
        var item = new Row("computed") { Number = 7, Label = "first" };
        var helper = new ExcelHelper();
        _ = helper.Export([item], "Rows", Map);
        item.Label = "changed";
        using var input = new MemoryStream(helper.Export([item], "Rows", Map));
        using var workbook = new XSSFWorkbook(input);
        var sheet = workbook.GetSheetAt(0);
        Assert.Equal(new[] { "Label", "Number", "Optional", "Date", "Id", "Status", "ReadOnly" },
            sheet.GetRow(0).Cells.Select(c => c.StringCellValue));
        Assert.Equal("changed", sheet.GetRow(1).GetCell(0).StringCellValue);
        Assert.Equal("computed", sheet.GetRow(1).GetCell(6).StringCellValue);
    }

    [Fact]
    public void Import_MatchesReorderedHeaders_UsesFormulaDateNullableEnumAndCustomConverters()
    {
        Guid id = Guid.NewGuid();
        using var source = new XSSFWorkbook();
        var sheet = source.CreateSheet("Rows");
        string[] headers = ["Status", "Id", "Optional", "Number", "Date", "ReadOnly", "Label", "Unknown"];
        var title = sheet.CreateRow(0);
        for (int i = 0; i < headers.Length; i++) title.CreateCell(i).SetCellValue(headers[i]);
        var row = sheet.CreateRow(1);
        row.CreateCell(0).SetCellValue("ready");
        row.CreateCell(1).SetCellValue(id.ToString());
        row.CreateCell(2).SetCellValue(23);
        row.CreateCell(3).SetCellFormula("2+3");
        var date = new DateTime(2025, 4, 8);
        row.CreateCell(4).SetCellValue(date);
        row.CreateCell(5).SetCellValue("ignored");
        row.CreateCell(6).SetCellValue("hello");
        row.CreateCell(7).SetCellValue("ignored");
        using var data = new MemoryStream();
        source.Write(data, leaveOpen: true);
        var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
        try
        {
            var result = Assert.Single(helper.Import("rows.xlsx", data, Map));
            Assert.Equal(5, result.Number);
            Assert.Equal(23, result.Optional);
            Assert.Equal(date, result.Date);
            Assert.Equal(id, result.Id);
            Assert.Equal(Status.Ready, result.Status);
            Assert.Equal("factory", result.ReadOnly);
            Assert.Equal("HELLO", result.Label);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData("1.9")]
    [InlineData("2147483648")]
    [InlineData(" ")]
    [InlineData("invalid")]
    public void Import_InvalidIntegerPreservesFactoryValue(string value)
    {
        using var source = new XSSFWorkbook();
        var sheet = source.CreateSheet("Rows");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("Number");
        sheet.CreateRow(1).CreateCell(0).SetCellValue(value);
        using var data = new MemoryStream();
        source.Write(data, leaveOpen: true);
        var helper = new ExcelHelper().InitStartReadRowIndex(0, 1);
        try { Assert.Equal(41, Assert.Single(helper.Import("rows.xlsx", data, Map)).Number); }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void Import_EnumUnderlyingTypesAndNullableEnums_KeepDirectNumericAndFormulaPath()
    {
        var map = Maps.EnumRow;
        using var source = new XSSFWorkbook();
        var sheet = source.CreateSheet("Enums");
        string[] headers = ["Byte", "Short", "Long", "Optional"];
        var title = sheet.CreateRow(0);
        for (int i = 0; i < headers.Length; i++) title.CreateCell(i).SetCellValue(headers[i]);
        var row = sheet.CreateRow(1);
        row.CreateCell(0).SetCellValue(255);
        row.CreateCell(1).SetCellValue(-2);
        row.CreateCell(2).SetCellValue(2147483648);
        row.CreateCell(3).SetCellFormula("2-1");
        using var data = new MemoryStream();
        source.Write(data, leaveOpen: true);
        var helper = new ExcelHelper().InitStartReadRowIndex(0, 1);
        try
        {
            var result = Assert.Single(helper.Import("enums.xlsx", data, map));
            Assert.Equal(ByteStatus.Maximum, result.Byte);
            Assert.Equal(ShortStatus.Negative, result.Short);
            Assert.Equal(LongStatus.Large, result.Long);
            Assert.Equal(Status.Ready, result.Optional);
        }
        finally { helper.Workbook?.Dispose(); }
    }
    [ExcelSerializable(typeof(Row), Factory = "CreateRow")]
    [ExcelSerializable(typeof(EnumRow))]
    private static partial class Maps
    {
        private static Row CreateRow() => new("factory");
    }
}
