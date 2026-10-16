using System.Globalization;
using System.Text;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class CsvImportReportTests
{
    public sealed class Product
    {
        [ExcelColumn("编号", Required = true, Order = 0)] public int Id { get; set; }
        [ExcelColumn("名称", Required = true, Order = 1)] public string Name { get; set; } = "default";
        [ExcelColumn("价格", Required = true, Order = 2)] public decimal Price { get; set; }
        [ExcelColumn("启用", Required = true, Order = 3)] public bool Enabled { get; set; }
        [ExcelColumn("数量", Order = 4)] public int? Quantity { get; set; }
        [ExcelColumn(Ignore = true)] public string Ignored { get; set; } = "ignored";
    }

    [Fact]
    public async Task ExportAndImportReuseColumnMappingAndInvariantValuesLeavingStreamsOpen()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var helper = new ExcelHelper();
            using var output = new MemoryStream();
            var source = new Product { Id = 0, Name = "中文,\"引号\"\n换行", Price = 12.34m, Enabled = false };
            await helper.ExportCsvAsync(new[] { source }, output);
            Assert.True(output.CanWrite);
            Assert.StartsWith("编号,名称,价格,启用,数量\r\n", Encoding.UTF8.GetString(output.ToArray()));
            output.Position = 0;
            var report = await helper.ImportCsvWithReportAsync<Product>(output);
            Assert.True(report.IsValid);
            var row = Assert.Single(report.Rows);
            Assert.Equal(2, row.RowNumber);
            Assert.Equal(source.Name, row.Value.Name);
            Assert.Equal(12.34m, row.Value.Price);
            Assert.Equal(0, row.Value.Id);
            Assert.False(row.Value.Enabled);
            Assert.Null(row.Value.Quantity);
            Assert.Equal("ignored", row.Value.Ignored);
            Assert.True(output.CanRead);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public async Task ReorderedColumnsAndInvalidRowsRetainSourcePositionsAndOnlyValidateConvertedRows()
    {
        using var input = Csv("名称,价格,编号,启用,数量\n正常,1.2,1,true,\n ,bad,wrong,false,\n负数,-1,2,true,\n\"多\n行\",2,3,false,0");
        var validated = new List<int>();
        var report = await new ExcelHelper().ImportCsvWithReportAsync<Product>(input, value =>
        {
            validated.Add(value.Id);
            return value.Price < 0 ? [new("价格不能为负数。", nameof(Product.Price))] : [];
        });
        Assert.False(report.IsValid);
        Assert.Equal(new[] { 2, 5 }, report.Rows.Select(row => row.RowNumber));
        Assert.Equal(new[] { 1, 2, 3 }, validated);
        Assert.Equal(4, report.Errors.Count);
        Assert.All(report.Errors, error => Assert.Equal("CSV", error.SheetName));
        Assert.Equal(new[] { 1, 2, 3 }, report.Errors.Where(error => error.RowNumber == 3).Select(error => error.ColumnNumber!.Value));
        var business = Assert.Single(report.Errors, error => error.Kind == ExcelImportErrorKind.BusinessValidation);
        Assert.Equal(4, business.RowNumber);
        Assert.Equal(2, business.ColumnNumber);
        Assert.Equal(nameof(Product.Price), business.PropertyName);
        Assert.Equal("wrong", report.Errors.Single(error => error.PropertyName == nameof(Product.Id)).RawValue);
    }

    [Theory]
    [InlineData("编号,名称,启用\n1,name,true", ExcelImportErrorKind.MissingColumn)]
    [InlineData("编号,名称,价格,价格,启用\n1,name,1,2,true", ExcelImportErrorKind.DuplicateColumn)]
    public async Task InvalidHeadersPreventRowValidation(string csv, ExcelImportErrorKind expected)
    {
        using var input = Csv(csv);
        var report = await new ExcelHelper().ImportCsvWithReportAsync<Product>(input,
            _ => throw new InvalidOperationException("Must not validate rows with invalid headers."));
        Assert.Empty(report.Rows);
        var error = Assert.Single(report.Errors);
        Assert.Equal(expected, error.Kind);
        Assert.Equal(1, error.RowNumber);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task RowLimitCountsRejectedRowsAndAllowsExactBoundary()
    {
        using var input = Csv("编号,名称,价格,启用\nwrong,name,1,true\n1,name,2,true");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExcelHelper().ImportCsvWithReportAsync<Product>(input, maximumRows: 1));
        Assert.True(input.CanRead);
        input.Position = 0;
        var report = await new ExcelHelper().ImportCsvWithReportAsync<Product>(input, maximumRows: 2);
        Assert.Single(report.Rows);
        Assert.Single(report.Errors);
    }

    [Theory]
    [InlineData("编号,名称,价格,启用\n1,name,2", 4)]
    [InlineData("编号,名称,价格,启用\n1,name,2,true,extra", 5)]
    public async Task InconsistentRecordWidthsAreLocatedSyntaxErrors(string csv, int column)
    {
        using var input = Csv(csv);
        var error = await Assert.ThrowsAsync<CsvFormatException>(() => new ExcelHelper().ImportCsvWithReportAsync<Product>(input));
        Assert.Equal(2, error.RowNumber);
        Assert.Equal(column, error.ColumnNumber);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task CancellationAndApplicationFailuresPropagateWithoutClosingInput()
    {
        using var input = Csv("编号,名称,价格,启用\n1,name,1,true");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExcelHelper().ImportCsvWithReportAsync<Product>(input,
            cancellationToken: cancellation.Token));
        Assert.Equal(0, input.Position);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExcelHelper().ImportCsvWithReportAsync<Product>(input,
            _ => throw new InvalidOperationException("business failure")));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task EmptyInputIsRejectedButHeaderOnlyRepresentsAnEmptyTable()
    {
        using var empty = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExcelHelper().ImportCsvWithReportAsync<Product>(empty));
        using var header = Csv("编号,名称,价格,启用");
        var report = await new ExcelHelper().ImportCsvWithReportAsync<Product>(header);
        Assert.True(report.IsValid);
        Assert.Empty(report.Rows);
    }

    private static MemoryStream Csv(string text) => new(Encoding.UTF8.GetBytes(text));
}
