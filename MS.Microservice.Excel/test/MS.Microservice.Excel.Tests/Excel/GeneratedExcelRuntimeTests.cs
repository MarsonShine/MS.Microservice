using MS.Microservice.Excel.Aot;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;
using Xunit.Abstractions;
using Old = MS.Microservice.Lab.AotExamples.Legacy.Excel.ManualMapping;

namespace MS.Microservice.Excel.Tests.Generation;

public sealed partial class GeneratedExcelRuntimeTests(ITestOutputHelper output)
{
    [Fact]
    public void InheritedAndReadOnlyMembersKeepTheirDeclaredContracts()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet();
        string[] names = ["标识", "Name", "ReadOnly", "InitOnly", "PrivateSetter", "Hidden", "Item"];
        for (var i = 0; i < names.Length; i++)
        {
            (sheet.GetRow(0) ?? sheet.CreateRow(0)).CreateCell(i).SetCellValue(names[i]);
            (sheet.GetRow(1) ?? sheet.CreateRow(1)).CreateCell(i).SetCellValue(i == 1 ? "中文" : "42");
        }
        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
        try
        {
            var row = Assert.Single(helper.Import("rows.xlsx", stream, Maps.DerivedRow));
            Assert.Equal(42, row.Id);
            Assert.Equal("中文", row.Name);
            Assert.Equal(7, row.ReadOnly);
            Assert.Equal(11, row.InitOnly);
            Assert.Equal(13, row.PrivateSetter);
            Assert.Equal("hidden", row.ReadHidden());
            using var exported = new XSSFWorkbook(new MemoryStream(helper.Export([row], "Rows", Maps.DerivedRow)));
            Assert.Equal(new[] { "标识", "Name", "ReadOnly", "InitOnly", "PrivateSetter" },
                exported.GetSheetAt(0).GetRow(0).Cells.Select(c => c.StringCellValue));
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(128UL)]
    public void NullableFlagsAndLongFormattedNamesRoundTrip(ulong? bits)
    {
        var value = bits.HasValue ? (LongFlags?)bits.Value : null;
        var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
        var bytes = helper.Export([new FlagRow { Id = 1, Flags = value }], "Rows", Maps.FlagRow);
        using var workbook = new XSSFWorkbook(new MemoryStream(bytes));
        var cell = workbook.GetSheetAt(0).GetRow(1).GetCell(1);
        if (value.HasValue) Assert.Equal(value.Value.ToString(), cell.StringCellValue);
        else Assert.Null(cell);
        try { Assert.Equal(value, Assert.Single(helper.Import(bytes, Maps.FlagRow)).Flags); }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void GeneratedNumericAccessAvoidsPerCellBoxing()
    {
        using var workbook = new HSSFWorkbook();
        var row = workbook.CreateSheet().CreateRow(0);
        var cell = row.CreateCell(0);
        cell.SetCellValue(42);
        var reader = new ExcelCellReader(cell, new DataFormatter(), workbook.GetCreationHelper().CreateFormulaEvaluator());
        var model = new NumberRow { Value = 42 };
        var column = Maps.NumberRow.Slots[0];
        var legacy = Old.ExcelColumn<NumberRow>.Create("Value", static r => r.Value, static (r, v) => r.Value = v, Old.ExcelValueConverters.Int32);
        for (var i = 0; i < 1000; i++)
        {
            column.Write(model, row, 0, null);
            column.Read!(model, reader);
            cell.SetCellValue((int)legacy.Getter(model)!);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) cell.SetCellValue((int)legacy.Getter(model)!);
        var oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            column.Write(model, row, 0, null);
            column.Read!(model, reader);
        }
        var generatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(42, model.Value);
        Assert.Equal(42, cell.NumericCellValue);
        Assert.Equal(0, generatedBytes);
        output.WriteLine($"10,000 warmed cells: old object getter={oldBytes} bytes; generated write+read={generatedBytes} bytes.");
    }

    private class BaseRow
    {
        [ExcelColumn("标识", Order = -1)]
        public int Id { get; set; }
    }
    private sealed class DerivedRow : BaseRow
    {
        public string? Name { get; set; }
        public int ReadOnly => 7;
        public int InitOnly { get; init; } = 11;
        public int PrivateSetter { get; private set; } = 13;
        public string Hidden { private get; set; } = "hidden";
        public string ReadHidden() => Hidden;
        public string this[int index] => throw new InvalidOperationException();
        [ExcelColumn(Ignore = true)]
        public object Ignored => throw new InvalidOperationException();
    }
    private sealed class NumberRow { public int Value { get; set; } }
    private sealed class FlagRow { public int Id { get; set; } public LongFlags? Flags { get; set; } }
    [Flags]
    private enum LongFlags : ulong
    {
        None = 0,
        FirstFlagWithAnIntentionallyLongNameToExerciseFormattingBeyondTheStackBuffer = 1,
        SecondFlagWithAnIntentionallyLongNameToExerciseFormattingBeyondTheStackBuffer = 2
    }
    [ExcelSerializable(typeof(DerivedRow))]
    [ExcelSerializable(typeof(NumberRow))]
    [ExcelSerializable(typeof(FlagRow))]
    private static partial class Maps;
}
