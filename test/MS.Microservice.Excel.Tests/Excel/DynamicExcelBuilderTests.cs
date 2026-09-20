using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using FluentAssertions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Utils.Excel;

public sealed class DynamicExcelBuilderTests
{
    private static readonly ExcelModelMap<TemplateRow> Map = new(static () => new TemplateRow(),
        ExcelColumn<TemplateRow>.Create("编号", static r => r.Id, static (r, v) => r.Id = v, ExcelValueConverters.Int32),
        ExcelColumn<TemplateRow>.Create("名称", static r => r.Name, static (r, v) => r.Name = v, ExcelValueConverters.String),
        ExcelColumn<TemplateRow>.Create("启用", static r => r.Enabled, static (r, v) => r.Enabled = v, ExcelValueConverters.Boolean),
        ExcelColumn<TemplateRow>.Create("金额", static r => r.Amount, static (r, v) => r.Amount = v, ExcelValueConverters.Decimal),
        ExcelColumn<TemplateRow>.Create("日期", static r => r.Date, static (r, v) => r.Date = v, ExcelValueConverters.DateTime),
        ExcelColumn<TemplateRow>.Create("状态", static r => r.Status, static (r, v) => r.Status = v, ExcelValueConverters.Enum<RowStatus>()));

    [Fact]
    public async Task DynamicExcelBuilder_ShouldCopyStyles_WriteValues_AndWriteToPipe()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        var style = workbook.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.Yellow.Index;
        style.FillPattern = FillPattern.SolidForeground;

        IRow titleRow = sheet.CreateRow(0);
        CreateHeaderCell(titleRow, 0, "编号", style);
        CreateHeaderCell(titleRow, 1, "名称", style);
        CreateHeaderCell(titleRow, 2, "启用", style);
        CreateHeaderCell(titleRow, 3, "金额", style);
        CreateHeaderCell(titleRow, 4, "日期", style);
        CreateHeaderCell(titleRow, 5, "状态", style);

        var items = new List<TemplateRow>
        {
            new() { Id = 1, Name = "Alice", Enabled = true, Amount = 12.34m, Date = new DateTime(2024, 1, 2), Status = RowStatus.Ready }
        };

        var builder = new DynamicExcelBuilder<TemplateRow>(workbook, sheet, items, Map)
            .InitInsertRow(0, 1)
            .InsertCellValue(1);

        builder.Workbook.Should().BeSameAs(workbook);
        builder.Items.Should().BeSameAs(items);
        sheet.GetRow(1).GetCell(0).NumericCellValue.Should().Be(1);
        sheet.GetRow(1).GetCell(1).StringCellValue.Should().Be("Alice");
        sheet.GetRow(1).GetCell(2).BooleanCellValue.Should().BeTrue();
        sheet.GetRow(1).GetCell(3).NumericCellValue.Should().BeApproximately(12.34, 0.0001);
        sheet.GetRow(1).GetCell(5).StringCellValue.Should().Be("Ready");
        sheet.GetRow(1).GetCell(0).CellStyle.FillForegroundColor.Should().Be(style.FillForegroundColor);

        byte[] bytes = await builder.WriteAsync();
        var pipe = new Pipe();
        await builder.WriteAsync(pipe.Writer);
        await pipe.Writer.CompleteAsync();

        bytes.Should().NotBeEmpty();

        await using var copied = new MemoryStream();
        using (Stream readerStream = pipe.Reader.AsStream())
        {
            await readerStream.CopyToAsync(copied);
        }

        copied.Position = 0;
        using var copiedWorkbook = new XSSFWorkbook(copied);
        copiedWorkbook.GetSheet("S").GetRow(1).GetCell(1).StringCellValue.Should().Be("Alice");
    }

    [Fact]
    public void DynamicExcelBuilder_ShouldMapByDeclaredOrder_WhenTitleRowHasNoCells()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        sheet.CreateRow(0);

        var builder = new DynamicExcelBuilder<TemplateRow>(workbook, sheet, new[]
        {
            new TemplateRow { Id = 2, Name = "Bob", Enabled = false, Amount = 88.5m, Status = RowStatus.None }
        }, Map);

        builder.InitInsertRow(0, 1)
            .InsertCellValue(1);

        IRow row = sheet.GetRow(1);
        row.GetCell(0).NumericCellValue.Should().Be(2);
        row.GetCell(1).StringCellValue.Should().Be("Bob");
        row.GetCell(2).BooleanCellValue.Should().BeFalse();
        row.GetCell(3).NumericCellValue.Should().BeApproximately(88.5, 0.0001);
        row.GetCell(5).StringCellValue.Should().Be("None");
    }

    private static void CreateHeaderCell(IRow row, int index, string text, ICellStyle style)
    {
        ICell cell = row.CreateCell(index);
        cell.SetCellValue(text);
        cell.CellStyle = style;
    }

    [Fact]
    public void Template_UsesActualSparseHeaderIndexes_PreservesFooterAndReadsUpdatedValues()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        var title = sheet.CreateRow(0);
        title.CreateCell(2).SetCellValue("名称");
        title.CreateCell(5).SetCellValue("编号");
        title.CreateCell(7).SetCellValue("Unknown");
        sheet.CreateRow(1).CreateCell(0).SetCellValue("footer");
        var item = new TemplateRow { Id = 17, Name = "before" };
        var builder = new DynamicExcelBuilder<TemplateRow>(workbook, sheet, [item], Map).InitInsertRow(0, 1);
        item.Name = "after";
        builder.InsertCellValue(1);
        Assert.Equal("after", sheet.GetRow(1).GetCell(2).StringCellValue);
        Assert.Equal(17, sheet.GetRow(1).GetCell(5).NumericCellValue);
        Assert.Equal(CellType.Blank, sheet.GetRow(1).GetCell(7).CellType);
        Assert.Equal("footer", sheet.GetRow(2).GetCell(0).StringCellValue);
    }

    [Fact]
    public void EmptyTemplateSource_DoesNotInsertOrMoveRows()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("编号");
        sheet.CreateRow(1).CreateCell(0).SetCellValue("footer");
        new DynamicExcelBuilder<TemplateRow>(workbook, sheet, [], Map).InitInsertRow(0, 1).InsertCellValue(1);
        Assert.Equal(1, sheet.LastRowNum);
        Assert.Equal("footer", sheet.GetRow(1).GetCell(0).StringCellValue);
    }

    [Fact]
    public async Task OpenExcelAsync_PropagatesExplicitMapThroughPipeReader()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("名称");
        using var input = new MemoryStream();
        workbook.Write(input, leaveOpen: true);
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(input.ToArray());
        await pipe.Writer.CompleteAsync();
        var builder = await new ExcelHelper().OpenExcelAsync(pipe.Reader,
            new[] { new TemplateRow { Name = "mapped" } }, Map);
        using (builder.Workbook)
        {
            builder.InitInsertRow(0, 1).InsertCellValue(1);
            Assert.Equal("mapped", builder.Workbook.GetSheetAt(0).GetRow(1).GetCell(0).StringCellValue);
        }
        await pipe.Reader.CompleteAsync();
    }

    private sealed class TemplateRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool Enabled { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public RowStatus Status { get; set; }
    }

    private enum RowStatus
    {
        None = 0,
        Ready = 1
    }
}
