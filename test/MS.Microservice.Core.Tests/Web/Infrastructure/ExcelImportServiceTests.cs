using MS.Microservice.Web.Application.Uploads;
using NPOI.XSSF.UserModel;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class ExcelImportServiceTests
{
    [Fact]
    public async Task BuildBookClassificationSqlAsync_WithValidWorkbook_ReturnsExpectedStatements()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Books");
        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("BOOKID");
        header.CreateCell(1).SetCellValue("分类");
        var row = sheet.CreateRow(1);
        row.CreateCell(0).SetCellValue(42);
        row.CreateCell(1).SetCellValue("数字教辅、在线阅读");
        using var content = new MemoryStream();
        workbook.Write(content, leaveOpen: true);
        content.Position = 0;
        var service = new ExcelImportService();

        var sql = await service.BuildBookClassificationSqlAsync(
            "books.xlsx",
            content,
            CancellationToken.None);

        Assert.Contains(
            "INSERT INTO [dbo].[book_type]([BookId], [ClassifyId]) VALUES (42, 1);",
            sql);
        Assert.Contains(
            "INSERT INTO [dbo].[book_type]([BookId], [ClassifyId]) VALUES (42, 3);",
            sql);
        Assert.True(content.CanRead);
    }
}
