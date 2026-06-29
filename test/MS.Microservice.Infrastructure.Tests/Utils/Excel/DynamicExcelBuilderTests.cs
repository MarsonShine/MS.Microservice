using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using FluentAssertions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using MS.Microservice.Infrastructure.Utils.Excel;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Utils.Excel;

public sealed class DynamicExcelBuilderTests
{
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

        var builder = new DynamicExcelBuilder<TemplateRow>(workbook, sheet, items)
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
    public void DynamicExcelBuilder_ShouldMapByAttributeOrder_WhenTitleRowHasNoCells()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("S");
        sheet.CreateRow(0);

        var builder = new DynamicExcelBuilder<TemplateRow>(workbook, sheet, new[]
        {
            new TemplateRow { Id = 2, Name = "Bob", Enabled = false, Amount = 88.5m, Status = RowStatus.None }
        });

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

    private sealed class TemplateRow
    {
        [ExcelColumn(Name = "编号")]
        public int Id { get; set; }

        [ExcelColumn(Name = "名称")]
        public string? Name { get; set; }

        [ExcelColumn(Name = "启用")]
        public bool Enabled { get; set; }

        [ExcelColumn(Name = "金额")]
        public decimal Amount { get; set; }

        [ExcelColumn(Name = "日期")]
        public DateTime Date { get; set; }

        [ExcelColumn(Name = "状态")]
        public RowStatus Status { get; set; }
    }

    private enum RowStatus
    {
        None = 0,
        Ready = 1
    }
}
