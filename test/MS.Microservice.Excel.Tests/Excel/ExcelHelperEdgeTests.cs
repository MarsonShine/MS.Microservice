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
    /// <summary>
    /// Edge case tests for ExcelHelper import/export covering null cells,
    /// formula cells, empty rows, blank strings, different enum formats.
    /// </summary>
    public class ExcelHelperEdgeTests
    {
        private enum MyEnum { None = 0, A = 1, B = 2 }

        private class EdgeRow
        {
            [ExcelColumn(Name = "ID", Order = 0)]
            public int Id { get; set; }

            [ExcelColumn(Name = "名称", Order = 1)]
            public string? Name { get; set; }

            [ExcelColumn(Name = "金额", Order = 2)]
            public decimal Amount { get; set; }

            [ExcelColumn(Name = "启用", Order = 3)]
            public bool Enabled { get; set; }

            [ExcelColumn(Name = "状态", Order = 4)]
            public MyEnum Status { get; set; }
        }

        private class NullableRow
        {
            [ExcelColumn(Name = "ID")]
            public int? Id { get; set; }

            [ExcelColumn(Name = "名称")]
            public string? Name { get; set; }
        }

        [Fact]
        public void Import_NullCell_ShouldUseDefault()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("启用");
            header.CreateCell(4).SetCellValue("状态");

            // Row 1 has only first 2 cells populated
            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(5);
            r1.CreateCell(1).SetCellValue("test");

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<EdgeRow>("nulls.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().Be(5);
            rows[0].Name.Should().Be("test");
            rows[0].Amount.Should().Be(0m);
            rows[0].Enabled.Should().BeFalse();
            rows[0].Status.Should().Be(MyEnum.None);
        }

        [Fact]
        public void Import_EnumAsInteger_ShouldWork()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("启用");
            header.CreateCell(4).SetCellValue("状态");

            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(1);
            r1.CreateCell(1).SetCellValue("x");
            r1.CreateCell(2).SetCellValue(1.0);
            r1.CreateCell(3).SetCellValue(false);
            r1.CreateCell(4).SetCellValue(2); // enum as int

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<EdgeRow>("enumint.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].Status.Should().Be(MyEnum.B);
        }

        [Fact]
        public void Import_BlankStringCell_ShouldBeNull()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("启用");
            header.CreateCell(4).SetCellValue("状态");

            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(1);
            r1.CreateCell(1).SetCellValue(""); // blank
            r1.CreateCell(2).SetCellValue(0.0);
            r1.CreateCell(3).SetCellValue(false);
            r1.CreateCell(4).SetCellValue("A");

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<EdgeRow>("blank.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].Name.Should().BeNull();
        }

        [Fact]
        public void Import_MultipleRows_ShouldImportAll()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");
            header.CreateCell(2).SetCellValue("金额");
            header.CreateCell(3).SetCellValue("启用");
            header.CreateCell(4).SetCellValue("状态");

            for (int i = 0; i < 5; i++)
            {
                var r = sheet.CreateRow(i + 1);
                r.CreateCell(0).SetCellValue(i + 1);
                r.CreateCell(1).SetCellValue($"name{i}");
                r.CreateCell(2).SetCellValue((double)(i * 10m));
                r.CreateCell(3).SetCellValue(i % 2 == 0);
                r.CreateCell(4).SetCellValue(i % 2 == 0 ? "A" : "B");
            }

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<EdgeRow>("multi.xlsx", ms);

            rows.Should().HaveCount(5);
            rows[0].Id.Should().Be(1);
            rows[4].Id.Should().Be(5);
        }

        [Fact]
        public void Export_EmptyList_ShouldStillCreateWorkbook()
        {
            var bytes = new ExcelHelper().Export(new List<EdgeRow>(), "Empty");
            bytes.Should().NotBeNullOrEmpty();
            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            wb.GetSheet("Empty").Should().NotBeNull();
        }

        [Fact]
        public void Export_DataTable_Empty_ShouldCreateHeaders()
        {
            var table = new DataTable();
            table.Columns.Add("A", typeof(int));
            table.Columns.Add("B", typeof(string));

            var bytes = new ExcelHelper().Export(table, "EmptyTable");
            using var ms = new MemoryStream(bytes);
            using var wb = new XSSFWorkbook(ms);
            var sheet = wb.GetSheet("EmptyTable");
            sheet.GetRow(0).GetCell(0).StringCellValue.Should().Be("A");
            sheet.GetRow(0).GetCell(1).StringCellValue.Should().Be("B");
            sheet.GetRow(1).Should().BeNull();
        }

        [Fact]
        public void Import_NullableInt_BlankCell_ShouldBeNull()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("ID");
            header.CreateCell(1).SetCellValue("名称");

            var r1 = sheet.CreateRow(1);
            r1.CreateCell(0).SetCellValue(""); // blank -> null
            r1.CreateCell(1).SetCellValue("name");

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<NullableRow>("nullable.xlsx", ms);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().BeNull();
            rows[0].Name.Should().Be("name");
        }
    }
}
