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
            public int Id { get; set; }
            public string? Name { get; set; }
            public decimal Amount { get; set; }
            public bool Enabled { get; set; }
            public MyEnum Status { get; set; }
        }

        private class NullableRow
        {
            public int? Id { get; set; }
            public string? Name { get; set; }
        }

        private static readonly ExcelModelMap<EdgeRow> EdgeRowMap = new(static () => new EdgeRow(),
            ExcelColumn<EdgeRow>.Create("ID", static r => r.Id, static (r, v) => r.Id = v, ExcelValueConverters.Int32),
            ExcelColumn<EdgeRow>.Create("名称", static r => r.Name, static (r, v) => r.Name = v, ExcelValueConverters.String),
            ExcelColumn<EdgeRow>.Create("金额", static r => r.Amount, static (r, v) => r.Amount = v, ExcelValueConverters.Decimal),
            ExcelColumn<EdgeRow>.Create("启用", static r => r.Enabled, static (r, v) => r.Enabled = v, ExcelValueConverters.Boolean),
            ExcelColumn<EdgeRow>.Create("状态", static r => r.Status, static (r, v) => r.Status = v, ExcelValueConverters.Enum<MyEnum>()));

        private static readonly ExcelModelMap<NullableRow> NullableRowMap = new(static () => new NullableRow(),
            ExcelColumn<NullableRow>.Create("ID", static r => r.Id, static (r, v) => r.Id = v, ExcelValueConverters.Nullable(ExcelValueConverters.Int32)),
            ExcelColumn<NullableRow>.Create("名称", static r => r.Name, static (r, v) => r.Name = v, ExcelValueConverters.String));

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
                .Import<EdgeRow>("nulls.xlsx", ms, EdgeRowMap);

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
                .Import<EdgeRow>("enumint.xlsx", ms, EdgeRowMap);

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
                .Import<EdgeRow>("blank.xlsx", ms, EdgeRowMap);

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
                .Import<EdgeRow>("multi.xlsx", ms, EdgeRowMap);

            rows.Should().HaveCount(5);
            rows[0].Id.Should().Be(1);
            rows[4].Id.Should().Be(5);
        }

        [Fact]
        public void Export_EmptyList_ShouldStillCreateWorkbook()
        {
            var bytes = new ExcelHelper().Export(new List<EdgeRow>(), "Empty", EdgeRowMap);
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
                .Import<NullableRow>("nullable.xlsx", ms, NullableRowMap);

            rows.Should().HaveCount(1);
            rows[0].Id.Should().BeNull();
            rows[0].Name.Should().Be("name");
        }

        private class NoDefaultConstructorRow(string seed)
        {
            public int Id { get; set; } = int.Parse(seed);
        }

        [Fact]
        public void Import_ModelWithoutParameterlessConstructor_ShouldUseExplicitFactory()
        {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("S");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("ID");
            sheet.CreateRow(1).CreateCell(0).SetCellValue(1);

            using var ms = new MemoryStream();
            wb.Write(ms, leaveOpen: true);
            ms.Position = 0;

            var NoDefaultConstructorRowMap = new ExcelModelMap<NoDefaultConstructorRow>(
                static () => new NoDefaultConstructorRow("23"),
                ExcelColumn<NoDefaultConstructorRow>.Create("ID", static r => r.Id, static (r, v) => r.Id = v, ExcelValueConverters.Int32));
            var rows = new ExcelHelper()
                .InitSheetName("S")
                .InitStartReadRowIndex(0, 1)
                .Import<NoDefaultConstructorRow>("nocctor.xlsx", ms, NoDefaultConstructorRowMap);

            rows.Should().ContainSingle().Which.Id.Should().Be(1);
        }
    }
}
