using AotApi = MS.Microservice.Excel.Aot;
using LegacyApi = MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class ExcelCompatibilityTests
{
    private static readonly AotApi.ExcelModelMap<Row> Map = new(static () => new Row(),
        AotApi.ExcelColumn<Row>.Create("编号", static r => r.Id, static (r, v) => r.Id = v, AotApi.ExcelValueConverters.Int32),
        AotApi.ExcelColumn<Row>.Create("名称", static r => r.Name, static (r, v) => r.Name = v, AotApi.ExcelValueConverters.String),
        AotApi.ExcelColumn<Row>.Create("金额", static r => r.Amount, static (r, v) => r.Amount = v, AotApi.ExcelValueConverters.Nullable(AotApi.ExcelValueConverters.Decimal)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ActualLegacyAndAotWorkbooksAreReadableByBothImplementations(int count)
    {
        var input = Enumerable.Range(0, count).Select(i => new Row
        {
            Id = i + 1, Name = i == 1 ? null : "中文 <&>", Amount = i == 1 ? null : 12.5m
        }).ToList();
        byte[][] workbooks = [new LegacyApi.ExcelHelper().Export(input, "Rows"), new AotApi.ExcelHelper().Export(input, "Rows", Map)];
        foreach (var bytes in workbooks)
        {
            using var workbook = new XSSFWorkbook(new MemoryStream(bytes));
            var sheet = workbook.GetSheetAt(0);
            Assert.Equal(new[] { "编号", "名称", "金额" }, sheet.GetRow(0).Cells.Select(cell => cell.StringCellValue));
            var oldRows = new LegacyApi.ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1).Import<Row>(bytes);
            var newRows = new AotApi.ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1).Import(bytes, Map);
            Assert.Equal(input.Select(Values), oldRows.Select(Values));
            Assert.Equal(input.Select(Values), newRows.Select(Values));
        }
    }

    [Fact]
    public void OldAndNewColorMapsKeepTheSameNamesAndIndexes()
    {
        var legacy = new ExcelColorMap();
        var current = new AotApi.ExcelColorMap();
        Assert.Equal(legacy.Colors!.OrderBy(pair => pair.Key), current.Colors.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void MiniExcelEntryPointStillSupportsItsOriginalRoundTrip()
    {
        var helper = new LegacyApi.MiniExcelHelper();
        var bytes = helper.Export(new List<PlainRow> { new() { Id = 7, Name = "中文" } }, "Rows");
        var rows = helper.Import<PlainRow>(bytes);
        var row = Assert.Single(rows);
        Assert.Equal(7, row.Id);
        Assert.Equal("中文", row.Name);
    }

    [Fact]
    public void LegacyAccessorBridgeKeepsReadOnlyInitPrivateAndIndexedMemberRules()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Rows");
        string[] names = ["Id", "ReadOnly", "PrivateSetter", "InitOnly", "Hidden", "Item"];
        var header = sheet.CreateRow(0);
        var values = sheet.CreateRow(1);
        for (var i = 0; i < names.Length; i++)
        {
            header.CreateCell(i).SetCellValue(names[i]);
            values.CreateCell(i).SetCellValue(42);
        }
        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        var row = Assert.Single(new LegacyApi.ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1)
            .Import<AccessorRow>(stream.ToArray()));
        Assert.Equal(42, row.Id);
        Assert.Equal(7, row.ReadOnly);
        Assert.Equal(9, row.PrivateSetter);
        Assert.Equal(11, row.InitOnly);
        Assert.Equal(13, row.ReadHidden());
    }

    private static (int, string?, decimal?) Values(Row row) => (row.Id, row.Name, row.Amount);

    public sealed class Row
    {
        [ExcelColumn("名称", Order = 1)]
        public string? Name { get; set; }
        [ExcelColumn("金额", Order = 2)]
        public decimal? Amount { get; set; }
        [ExcelColumn("编号", Order = 0)]
        public int Id { get; set; }
        [ExcelColumn(Ignore = true)]
        public string Ignored => throw new InvalidOperationException("An ignored getter must not run.");
    }

    public sealed class PlainRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public sealed class AccessorRow
    {
        public int Id { get; set; }
        public int ReadOnly => 7;
        public int PrivateSetter { get; private set; } = 9;
        public int InitOnly { get; init; } = 11;
        public int Hidden { private get; set; } = 13;
        public int ReadHidden() => Hidden;
        public int this[int index]
        {
            get => throw new InvalidOperationException("Indexer must not be read.");
            set => throw new InvalidOperationException("Indexer must not be written.");
        }
    }
}
