using MS.Microservice.Excel.Aot;
using MS.Microservice.Lab.Application.Models;
using System.Text;

namespace MS.Microservice.Lab.Application.Uploads;

public interface IExcelImportService
{
    Task<string> BuildBookClassificationSqlAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);
}

public sealed partial class ExcelImportService : IExcelImportService
{
    private static readonly ExcelModelMap<ExcelBookClassificationRow> ExcelBookClassificationRowMap = Maps.ExcelBookClassificationRow;

    public async Task<string> BuildBookClassificationSqlAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("Excel 内容流必须可读。", nameof(content));
        }

        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        buffered.Position = 0;

        var excelHelper = new ExcelHelper()
            .InitSheetIndex(0)
            .InitStartReadRowIndex(0, 1);
        List<ExcelBookClassificationRow> rows;
        try
        {
            rows = excelHelper.Import<ExcelBookClassificationRow>(Path.GetFileName(fileName), buffered, ExcelBookClassificationRowMap);
        }
        finally
        {
            excelHelper.Workbook?.Dispose();
        }

        var sql = new StringBuilder();
        foreach (var row in rows)
        {
            var request = new ExcelDemoRequest
            {
                BookId = row.BookId,
                Classify = row.Classification
            };
            foreach (var bookClassify in request.BookClassify)
            {
                sql.AppendLine(
                    $"INSERT INTO [dbo].[book_type]([BookId], [ClassifyId]) VALUES ({row.BookId}, {(int)bookClassify});");
            }
        }

        return sql.ToString();
    }

    private sealed class ExcelBookClassificationRow
    {
        [ExcelColumn("BOOKID")]
        public int BookId { get; set; }
        [ExcelColumn("分类")]
        public string? Classification { get; set; }
    }
    [ExcelSerializable(typeof(ExcelBookClassificationRow))]
    private static partial class Maps;
}
