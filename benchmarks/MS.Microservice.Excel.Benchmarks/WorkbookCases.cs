using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.XSSF.UserModel;

internal static class WorkbookCases
{
    public static void Run()
    {
        foreach (int count in new[] { 1, 128 })
        {
            var models = Enumerable.Range(1, count).Select(id => new BenchmarkWorkbookRow { Id = id }).ToArray();
            Bench.Measure($"XLSX template styles / {count} x 8", count == 1 ? 16 : 4,
                () => CopyTemplateStyles(models),
                result => result.Created == count && result.LastStyle == result.TemplateStyle);
        }

        MeasureImport("XLSX import / dense 128", Enumerable.Range(1, 128).ToArray());
        MeasureImport("XLSX import / rows 1,40000,80000", [1, 40000, 80000]);
    }

    private static (int Created, short LastStyle, short TemplateStyle) CopyTemplateStyles(BenchmarkWorkbookRow[] models)
    {
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("Data");
        var header = sheet.CreateRow(0);
        var style = book.CreateCellStyle();
        style.WrapText = true;
        for (int col = 0; col < 8; col++)
        {
            var cell = header.CreateCell(col);
            cell.SetCellValue(col == 0 ? "Id" : $"Col{col}");
            cell.CellStyle = style;
        }

        new DynamicExcelBuilder<BenchmarkWorkbookRow>(book, sheet, models).InitInsertRow(0, 1);
        return (sheet.PhysicalNumberOfRows - 1, sheet.GetRow(models.Length).GetCell(7).CellStyle.Index, style.Index);
    }

    private static void MeasureImport(string name, int[] positions)
    {
        using var input = new MemoryStream();
        using (var book = new XSSFWorkbook())
        {
            var sheet = book.CreateSheet("Data");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("Id");
            foreach (int row in positions)
                sheet.CreateRow(row).CreateCell(0).SetCellValue(row);
            book.Write(input, leaveOpen: true);
        }

        Bench.Measure(name, 4, () =>
        {
            var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
            try { return helper.Import<BenchmarkWorkbookRow>("data.xlsx", input); }
            finally { helper.Workbook?.Dispose(); }
        }, rows => rows.Select(row => row.Id).SequenceEqual(positions));
    }

    private sealed class BenchmarkWorkbookRow
    {
        [ExcelColumn(Name = "Id")]
        public int Id { get; set; }
    }
}
