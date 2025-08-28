using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using System.Threading.Tasks;

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
                new MixedRow { Skip = "S", Keep = 2, Display = "D", Tail = new DateTime(2024,3,3) }
            };
            var bytes = helper.Export(data, "S");

            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("S");
            var header = sheet.GetRow(0);

            // 不应包含 Skip（Ignore=true）
            var headers = new[]
            {
                header.GetCell(0).StringCellValue,
                header.GetCell(1).StringCellValue,
                header.GetCell(2).StringCellValue
            };

            headers.Should().Contain("Keep");
            headers.Should().Contain("显示名");
            headers.Should().Contain("Tail"); // 未标注 => 属性名
        }

        private class MixedRow
        {
            [ExcelColumn(Ignore = true)]
            public string Skip { get; set; } = string.Empty;

            public int Keep { get; set; }

            [ExcelColumn(Name = "显示名", Order = 1)]
            public string Display { get; set; } = string.Empty;

            // 未标注 => 使用属性名，Order = int.MaxValue
            public DateTime Tail { get; set; }
        }

        [Fact]
        public void Export_ShouldWriteHeadersAndValues_Correctly()
        {
            // Arrange
            var data = new List<SampleRow>
            {
                new SampleRow
                {
                    Id = 1, Name = "张三", Amount = 12.34m,
                    Date = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
                    Enabled = true, Status = MyEnum.B
                }
            };

            var helper = new ExcelHelper();

            // Act
            var bytes = helper.Export(data, "Sheet1");

            // Assert
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
            // 日期：NPOI 写入为日期单元格，读取时应能识别
            var cellDate = row1.GetCell(3);
            DateUtil.IsCellDateFormatted(cellDate).Should().BeTrue();
            cellDate.DateCellValue!.Value.Date.Should().Be(new DateTime(2024, 1, 2));

            row1.GetCell(4).BooleanCellValue.Should().BeTrue();
            row1.GetCell(5).StringCellValue.Should().Be("B");
        }

        [Fact]
        public void Import_ShouldParseValues_Correctly()
        {
            // Arrange: 构造一个内存工作簿
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("SheetA");

            // 标题
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            // 内容
            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(2);                 // int
            r1.CreateCell(1).SetCellValue("李四");             // string
            r1.CreateCell(2).SetCellValue((double)99.99m);     // decimal->double
            var cDate = r1.CreateCell(3);
            cDate.SetCellValue(new DateTime(2024, 5, 6));
            var dateStyle = wb.CreateCellStyle();
            var format = wb.CreateDataFormat();
            dateStyle.DataFormat = format.GetFormat("yyyy-mm-dd");
            cDate.CellStyle = dateStyle;                       // 明确日期样式
            r1.CreateCell(4).SetCellValue(false);              // bool
            r1.CreateCell(5).SetCellValue("A");                // enum as string

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var helper = new ExcelHelper()
                .InitSheetName("SheetA")
                .InitStartReadRowIndex(0, 1);

            // Act
            var rows = helper.Import<SampleRow>("test.xlsx", ms);

            // Assert
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
        public void BuildColumnMap_ShouldMatchHeaders()
        {
            // Arrange
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            // Act
            var map = ExcelHelper.BuildColumnMap<SampleRow>(sheet, 0);

            // Assert
            map.Should().NotBeNull();
            map.Should().HaveCount(6);
            // 验证关键列索引
            map.Should().ContainKey(typeof(SampleRow).GetProperty(nameof(SampleRow.Id))!);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Id))!].Should().Be(0);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Name))!].Should().Be(1);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Amount))!].Should().Be(2);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Date))!].Should().Be(3);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Enabled))!].Should().Be(4);
            map[typeof(SampleRow).GetProperty(nameof(SampleRow.Status))!].Should().Be(5);
        }

        [Fact]
        public void Import_ShouldWorkWhenSomeColumnsMissing()
        {
            // Arrange: 缺少“状态”列
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

            // Act
            var rows = helper.Import<SampleRow>("missing.xlsx", ms);

            // Assert: 正常导入，未映射的属性为默认值
            rows.Should().HaveCount(1);
            var row = rows[0];
            row.Id.Should().Be(3);
            row.Name.Should().Be("王五");
            row.Amount.Should().Be(10.5m);
            row.Date.Date.Should().Be(new DateTime(2023, 12, 31));
            row.Enabled.Should().BeTrue();
            row.Status.Should().Be(MyEnum.None); // 缺列 => 默认值
        }

        [Fact]
        public async Task DynamicExcelBuilder_ShouldInsertRowsAccordingToColumnMap()
        {
            // Arrange: 模板工作簿，只有表头
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Data");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("日期");
            header.CreateCell(4).SetCellValue("启用");
            header.CreateCell(5).SetCellValue("状态");

            using var template = new MemoryStream();
            wb.Write(template, leaveOpen: true);
            template.Position = 0;

            var items = new List<SampleRow>
            {
                new SampleRow { Id = 10, Name = "A", Amount = 1.2m, Date = new DateTime(2024,1,1), Enabled = true, Status = MyEnum.B },
                new SampleRow { Id = 11, Name = "B", Amount = 3.4m, Date = new DateTime(2024,1,2), Enabled = false, Status = MyEnum.A }
            };

            var excel = new ExcelHelper();
            var builder = excel.OpenExcel(items, template, "Data", titleRowIndex: 0);

            // Act
            builder.InitInsertRow(startRowIndex: 1)
                   .InsertCellValue(startRowIndex: 1);
            var outBytes = await builder.WriteAsync();

            // Assert
            using var ms = new MemoryStream(outBytes);
            using var outWb = new XSSFWorkbook(ms);
            var outSheet = outWb.GetSheet("Data");

            var r1 = outSheet.GetRow(1);
            r1.GetCell(0).StringCellValue.Should().Be("10");
            r1.GetCell(1).StringCellValue.Should().Be("A");
            r1.GetCell(2).StringCellValue.Should().Be("1.2");
            r1.GetCell(3).StringCellValue.Should().Be(new DateTime(2024, 1, 1).ToString()); // DynamicExcelBuilder 按字符串写入
            r1.GetCell(4).StringCellValue.Should().Be("True");
            r1.GetCell(5).StringCellValue.Should().Be("B");

            var r2 = outSheet.GetRow(2);
            r2.GetCell(0).StringCellValue.Should().Be("11");
            r2.GetCell(1).StringCellValue.Should().Be("B");
            r2.GetCell(2).StringCellValue.Should().Be("3.4");
            r2.GetCell(3).StringCellValue.Should().Be(new DateTime(2024, 1, 2).ToString());
            r2.GetCell(4).StringCellValue.Should().Be("False");
            r2.GetCell(5).StringCellValue.Should().Be("A");
        }
    }
}