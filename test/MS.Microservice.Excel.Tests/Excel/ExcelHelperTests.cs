using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Utils.Excel
{
    public class ExcelHelperTests
    {
        private enum MyEnum
        {
            None = 0,
            A = 1,
            B = 2
        }

        private class SampleRow
        {
            [ExcelColumn(Name = "ID", Order = 0)]
            public int Id { get; set; }

            [ExcelColumn(Name = "名称", Order = 1)]
            public string? Name { get; set; }

            [ExcelColumn(Name = "金额", Order = 2)]
            public decimal Amount { get; set; }

            [ExcelColumn(Name = "日期", Order = 3)]
            public DateTime Date { get; set; }

            [ExcelColumn(Name = "启用", Order = 4)]
            public bool Enabled { get; set; }

            [ExcelColumn(Name = "状态", Order = 5)]
            public MyEnum Status { get; set; }
        }

        private class PlainRow
        {
            public int A { get; set; }
            public string? B { get; set; }
            public DateTime C { get; set; }
        }

        [Fact]
        public void Export_WithoutAttributes_ShouldUsePropertyNamesAsHeaders()
        {
            var helper = new ExcelHelper();
            var data = new List<PlainRow>
            {
                new PlainRow { A = 1, B = "x", C = new DateTime(2024, 1, 1) }
            };

            var bytes = helper.Export(data, "S");
            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("S");
            var header = sheet.GetRow(0);

            header.GetCell(0).StringCellValue.Should().Be("A");
            header.GetCell(1).StringCellValue.Should().Be("B");
            header.GetCell(2).StringCellValue.Should().Be("C");
        }

        [Fact]
        public void Import_WithoutAttributes_ShouldMapByPropertyNames()
        {
            var wb = new XSSFWorkbook();
            var s = wb.CreateSheet("S");
            var h = s.CreateRow(0);
            h.CreateCell(0).SetCellValue("A");
            h.CreateCell(1).SetCellValue("B");
            h.CreateCell(2).SetCellValue("C");

            var r1 = s.CreateRow(1);
            r1.CreateCell(0).SetCellValue(10);
            r1.CreateCell(1).SetCellValue("name");
            r1.CreateCell(2).SetCellValue(new DateTime(2024, 2, 2));

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<PlainRow>("t.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].A.Should().Be(10);
            rows[0].B.Should().Be("name");
            rows[0].C.Date.Should().Be(new DateTime(2024, 2, 2));
        }

        [Fact]
        public void MixedAttributes_ShouldHonorIgnore_And_DefaultToPropertyName()
        {
            var helper = new ExcelHelper();
            var data = new List<MixedRow>
            {
                new MixedRow { Skip = "S", Keep = 2, Display = "D", Tail = new DateTime(2024, 3, 3) }
            };
            var bytes = helper.Export(data, "S");

            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("S");
            var header = sheet.GetRow(0);

            var headers = new[]
            {
                header.GetCell(0).StringCellValue,
                header.GetCell(1).StringCellValue,
                header.GetCell(2).StringCellValue
            };

            headers.Should().Contain("Keep");
            headers.Should().Contain("显示名");
            headers.Should().Contain("Tail");
        }

        private class MixedRow
        {
            [ExcelColumn(Ignore = true)]
            public string Skip { get; set; } = string.Empty;

            public int Keep { get; set; }

            [ExcelColumn(Name = "显示名", Order = 1)]
            public string Display { get; set; } = string.Empty;

            public DateTime Tail { get; set; }
        }

        [Fact]
        public void Export_ShouldWriteHeadersAndValues_Correctly()
        {
            var data = new List<SampleRow>
            {
                new SampleRow
                {
                    Id = 1,
                    Name = "张三",
                    Amount = 12.34m,
                    Date = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
                    Enabled = true,
                    Status = MyEnum.B
                }
            };

            var helper = new ExcelHelper();
            var bytes = helper.Export(data, "Sheet1");

            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("Sheet1");
            sheet.Should().NotBeNull();

            var header = sheet.GetRow(0);
            header.GetCell(0).StringCellValue.Should().Be("ID");
            header.GetCell(1).StringCellValue.Should().Be("名称");
            header.GetCell(2).StringCellValue.Should().Be("金额");
            header.GetCell(3).StringCellValue.Should().Be("日期");
            header.GetCell(4).StringCellValue.Should().Be("启用");
            header.GetCell(5).StringCellValue.Should().Be("状态");

            var row1 = sheet.GetRow(1);
            row1.Should().NotBeNull();
            row1.GetCell(0).NumericCellValue.Should().Be(1);
            row1.GetCell(1).StringCellValue.Should().Be("张三");
            row1.GetCell(2).NumericCellValue.Should().BeApproximately((double)12.34m, 1e-8);

            var cellDate = row1.GetCell(3);
            DateUtil.IsCellDateFormatted(cellDate).Should().BeTrue();
            cellDate.DateCellValue!.Value.Date.Should().Be(new DateTime(2024, 1, 2));
            row1.GetCell(4).BooleanCellValue.Should().BeTrue();
            row1.GetCell(5).StringCellValue.Should().Be("B");
        }

        [Fact]
        public void Import_ShouldParseValues_Correctly()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("SheetA");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(2);
            r1.CreateCell(1).SetCellValue("李四");
            r1.CreateCell(2).SetCellValue((double)99.99m);
            var cDate = r1.CreateCell(3);
            cDate.SetCellValue(new DateTime(2024, 5, 6));
            var dateStyle = wb.CreateCellStyle();
            var format = wb.CreateDataFormat();
            dateStyle.DataFormat = format.GetFormat("yyyy-mm-dd");
            cDate.CellStyle = dateStyle;
            r1.CreateCell(4).SetCellValue(false);
            r1.CreateCell(5).SetCellValue("A");

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var helper = new ExcelHelper()
                .InitSheetName("SheetA")
                .InitStartReadRowIndex(0, 1);

            var rows = helper.Import<SampleRow>("test.xlsx", ms);

            rows.Should().HaveCount(1);
            var row = rows[0];
            row.Id.Should().Be(2);
            row.Name.Should().Be("李四");
            row.Amount.Should().Be(99.99m);
            row.Date.Date.Should().Be(new DateTime(2024, 5, 6));
            row.Enabled.Should().BeFalse();
            row.Status.Should().Be(MyEnum.A);
        }

        [Fact]
        public void Import_ShouldWorkWhenSomeColumnsMissing()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");

            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(3);
            r1.CreateCell(1).SetCellValue("王五");
            r1.CreateCell(2).SetCellValue(10.5);
            r1.CreateCell(3).SetCellValue(new DateTime(2023, 12, 31));
            r1.CreateCell(4).SetCellValue(true);

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var helper = new ExcelHelper()
                .InitSheetIndex(0)
                .InitStartReadRowIndex(0, 1);

            var rows = helper.Import<SampleRow>("missing.xlsx", ms);

            rows.Should().HaveCount(1);
            var row = rows[0];
            row.Id.Should().Be(3);
            row.Name.Should().Be("王五");
            row.Amount.Should().Be(10.5m);
            row.Date.Date.Should().Be(new DateTime(2023, 12, 31));
            row.Enabled.Should().BeTrue();
            row.Status.Should().Be(MyEnum.None);
        }

        [Fact]
        public async Task ExportAsync_ShouldWriteHeadersAndValues_Correctly()
        {
            var helper = new ExcelHelper();
            var data = new List<SampleRow>
            {
                new SampleRow
                {
                    Id = 7,
                    Name = "赵六",
                    Amount = 45.67m,
                    Date = new DateTime(2024, 6, 7),
                    Enabled = true,
                    Status = MyEnum.A
                }
            };

            await using var ms = new MemoryStream();
            await helper.ExportAsync(data, "AsyncSheet", ms);
            ms.Position = 0;

            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("AsyncSheet");

            sheet.GetRow(0).GetCell(0).StringCellValue.Should().Be("ID");
            sheet.GetRow(1).GetCell(0).NumericCellValue.Should().Be(7);
            sheet.GetRow(1).GetCell(1).StringCellValue.Should().Be("赵六");
            sheet.GetRow(1).GetCell(5).StringCellValue.Should().Be("A");
        }

        [Fact]
        public async Task ImportAsync_ShouldMapBySheetName_Correctly()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("AsyncImport");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue(8);
            row.CreateCell(1).SetCellValue("异步");
            row.CreateCell(2).SetCellValue(88.5);
            row.CreateCell(3).SetCellValue(new DateTime(2024, 7, 8));
            row.CreateCell(4).SetCellValue(true);
            row.CreateCell(5).SetCellValue("B");

            await using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = await new ExcelHelper()
                .InitSheetName("AsyncImport")
                .InitStartReadRowIndex(0, 1)
                .ImportAsync<SampleRow>("async.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().Be(8);
            rows[0].Name.Should().Be("异步");
            rows[0].Amount.Should().Be(88.5m);
            rows[0].Enabled.Should().BeTrue();
            rows[0].Status.Should().Be(MyEnum.B);
        }

        [Fact]
        public void Export_DataTable_ShouldWriteHeadersAndValues_Correctly()
        {
            var table = new DataTable();
            table.Columns.Add("ColA", typeof(int));
            table.Columns.Add("ColB", typeof(string));
            table.Rows.Add(1, "row1");

            var bytes = new ExcelHelper().Export(table, "TableSheet");

            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("TableSheet");

            sheet.GetRow(0).GetCell(0).StringCellValue.Should().Be("ColA");
            sheet.GetRow(0).GetCell(1).StringCellValue.Should().Be("ColB");
            sheet.GetRow(1).GetCell(0).NumericCellValue.Should().Be(1);
            sheet.GetRow(1).GetCell(1).StringCellValue.Should().Be("row1");
        }

        [Fact]
        public async Task ExportAsync_ToPipeWriter_ShouldWriteWorkbook()
        {
            var helper = new ExcelHelper();
            var pipe = new System.IO.Pipelines.Pipe();
            var data = new List<SampleRow>
            {
                new()
                {
                    Id = 9,
                    Name = "Pipe",
                    Amount = 66.5m,
                    Date = new DateTime(2024, 8, 9),
                    Enabled = true,
                    Status = MyEnum.B
                }
            };

            await helper.ExportAsync(data, "PipeSheet", pipe.Writer);
            await pipe.Writer.CompleteAsync();

            await using var ms = new MemoryStream();
            using (Stream readerStream = pipe.Reader.AsStream())
            {
                await readerStream.CopyToAsync(ms);
            }

            ms.Position = 0;
            using var workbook = new XSSFWorkbook(ms);
            workbook.GetSheet("PipeSheet").GetRow(1).GetCell(0).NumericCellValue.Should().Be(9);
            workbook.GetSheet("PipeSheet").GetRow(1).GetCell(1).StringCellValue.Should().Be("Pipe");
        }

        [Fact]
        public async Task ImportAsync_FromPipeReader_ShouldMapBySheetIndex()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("PipeImport");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue(11);
            row.CreateCell(1).SetCellValue("Reader");
            row.CreateCell(2).SetCellValue(11.5);
            row.CreateCell(3).SetCellValue(new DateTime(2024, 9, 10));
            row.CreateCell(4).SetCellValue(false);
            row.CreateCell(5).SetCellValue("A");

            await using var source = new MemoryStream();
            workbook.Write(source, leaveOpen: true);
            var pipe = new System.IO.Pipelines.Pipe();
            await pipe.Writer.WriteAsync(source.ToArray());
            await pipe.Writer.CompleteAsync();

            var rows = await new ExcelHelper()
                .InitSheetIndex(0)
                .InitStartReadRowIndex(0, 1)
                .ImportAsync<SampleRow>("pipe.xlsx", pipe.Reader);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().Be(11);
            rows[0].Name.Should().Be("Reader");
            rows[0].Status.Should().Be(MyEnum.A);
        }

        [Fact]
        public void Import_FromNonSeekableStream_ShouldWork()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("NonSeekable");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            var row = sheet.CreateRow(1);
            row.CreateCell(0).SetCellValue(12);
            row.CreateCell(1).SetCellValue("Stream");
            row.CreateCell(2).SetCellValue(12.5);
            row.CreateCell(3).SetCellValue(new DateTime(2024, 10, 11));
            row.CreateCell(4).SetCellValue(true);
            row.CreateCell(5).SetCellValue("B");

            using var source = new MemoryStream();
            workbook.Write(source, leaveOpen: true);
            using var nonSeekable = new NonSeekableStream(new MemoryStream(source.ToArray()));

            var rows = new ExcelHelper()
                .InitSheetName("NonSeekable")
                .InitStartReadRowIndex(0, 1)
                .Import<SampleRow>("non-seekable.xlsx", nonSeekable);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().Be(12);
            rows[0].Name.Should().Be("Stream");
            rows[0].Status.Should().Be(MyEnum.B);
        }

        [Fact]
        public void Import_WithMissingSheetName_ShouldThrow()
        {
            var workbook = new XSSFWorkbook();
            workbook.CreateSheet("Exists");
            using var stream = new MemoryStream();
            workbook.Write(stream, leaveOpen: true);
            stream.Position = 0;

            var action = () => new ExcelHelper()
                .InitSheetName("Missing")
                .InitStartReadRowIndex(0, 1)
                .Import<SampleRow>("missing.xlsx", stream);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*未找到名称为 Missing 的工作表*");
        }
    }
}
