using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MS.Microservice.Excel.Tests.Excel;

public sealed class ExcelModelMapTests
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
        public string ReadOnly => seed;
        public int Number { get; set; } = 41;
        public int? Optional { get; set; }
        public DateTime Date { get; set; }
        public Guid Id { get; set; }
        public Status Status { get; set; }
        public string? Label { get; set; }
        public string Undeclared => throw new InvalidOperationException("Must not discover this property.");
    }

    private static readonly ExcelModelMap<Row> Map = new(static () => new Row("factory"),
        ExcelColumn<Row>.Create("Label", static r => r.Label, static (r, v) => r.Label = v, new ExcelValueConverter<string?>(Uppercase)),
        ExcelColumn<Row>.Create(" Number ", static r => r.Number, static (r, v) => r.Number = v, ExcelValueConverters.Int32),
        ExcelColumn<Row>.Create("Optional", static r => r.Optional, static (r, v) => r.Optional = v, ExcelValueConverters.Nullable(ExcelValueConverters.Int32)),
        ExcelColumn<Row>.Create("Date", static r => r.Date, static (r, v) => r.Date = v, ExcelValueConverters.DateTime),
        ExcelColumn<Row>.Create("Id", static r => r.Id, static (r, v) => r.Id = v, ExcelValueConverters.Guid),
        ExcelColumn<Row>.Create("Status", static r => r.Status, static (r, v) => r.Status = v, ExcelValueConverters.Enum<Status>()),
        ExcelColumn<Row>.Create("ReadOnly", static r => r.ReadOnly, null, ExcelValueConverters.String));

    private static bool Uppercase(string text, out string? value)
    {
        value = text.ToUpperInvariant();
        return true;
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
    public void MappingCopiesColumnArray_AndExportsOnlyDeclaredColumns()
    {
        ExcelColumn<Row>[] columns = [ExcelColumn<Row>.Create("Number", static r => r.Number, null, ExcelValueConverters.Int32)];
        var map = new ExcelModelMap<Row>(static () => new Row("seed"), columns);
        columns[0] = ExcelColumn<Row>.Create("Label", static r => r.Label, null, ExcelValueConverters.String);
        using var data = new MemoryStream(new ExcelHelper().Export([new Row("seed")], "Rows", map));
        using var workbook = new XSSFWorkbook(data);
        Assert.Equal("Number", Assert.Single(workbook.GetSheetAt(0).GetRow(0).Cells).StringCellValue);
    }

    [Fact]
    public void Import_EnumUnderlyingTypesAndNullableEnums_KeepDirectNumericAndFormulaPath()
    {
        var map = new ExcelModelMap<EnumRow>(static () => new EnumRow(),
            ExcelColumn<EnumRow>.Create("Byte", static r => r.Byte, static (r, v) => r.Byte = v, ExcelValueConverters.Enum<ByteStatus>()),
            ExcelColumn<EnumRow>.Create("Short", static r => r.Short, static (r, v) => r.Short = v, ExcelValueConverters.Enum<ShortStatus>()),
            ExcelColumn<EnumRow>.Create("Long", static r => r.Long, static (r, v) => r.Long = v, ExcelValueConverters.Enum<LongStatus>()),
            ExcelColumn<EnumRow>.Create("Optional", static r => r.Optional, static (r, v) => r.Optional = v, ExcelValueConverters.Nullable(ExcelValueConverters.Enum<Status>())));
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
}
