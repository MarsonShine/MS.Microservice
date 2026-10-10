using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class ExcelImportReportTests
{
    private sealed class ImportRow
    {
        [ExcelColumn("ID", Required = true)]
        public int Id { get; set; }
        [ExcelColumn("名称", Required = true)]
        public string? Name { get; set; }
        [ExcelColumn("启用", Required = true)]
        public bool Enabled { get; set; }
        [ExcelColumn("数量")]
        public int? Quantity { get; set; }
    }

    [Fact]
    public void CollectsAllCellErrorsExcludesInvalidRowsAndPreservesOriginalRowNumbers()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"),
            [1, "valid", true, 5], ["not-an-int", " ", "not-a-bool", "bad"], [2, "later", false, null]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input);
            Assert.False(report.IsValid);
            Assert.Equal([2, 4], report.Rows.Select(row => row.RowNumber));
            Assert.Equal([1, 2], report.Rows.Select(row => row.Value.Id));
            Assert.Equal(4, report.Errors.Count);
            Assert.All(report.Errors, error => Assert.Equal(3, error.RowNumber));
            Assert.Equal([1, 2, 3, 4], report.Errors.Select(error => error.ColumnNumber));
            Assert.Equal([ExcelImportErrorKind.Conversion, ExcelImportErrorKind.Required,
                ExcelImportErrorKind.Conversion, ExcelImportErrorKind.Conversion], report.Errors.Select(error => error.Kind));
            Assert.Equal("not-an-int", report.Errors[0].RawValue);
            Assert.Equal(nameof(ImportRow.Id), report.Errors[0].PropertyName);
            Assert.Equal("Data", report.Errors[0].SheetName);
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void RequiredAcceptsZeroFalseAndOptionalBlankRetainsDefaults()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), [0, "valid", false, " "]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input);
            Assert.True(report.IsValid);
            var row = Assert.Single(report.Rows).Value;
            Assert.Equal(0, row.Id);
            Assert.False(row.Enabled);
            Assert.Null(row.Quantity);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void RequiredChecksSourceCellInsteadOfThePropertyInitializer(string? value)
    {
        using var input = Workbook(("名称", "unused", "unused2", "unused3"), [value]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<InitializedRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            Assert.Equal(ExcelImportErrorKind.Required, Assert.Single(report.Errors).Kind);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void MissingRequiredAndDuplicateMappedHeadersAreLocatedTemplateErrors()
    {
        using var input = Workbook(("ID", "ID", "数量", "unknown"), [1, 2, 3, "ignored"]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            Assert.Equal(3, report.Errors.Count);
            Assert.All(report.Errors, error => Assert.Equal(1, error.RowNumber));
            var duplicate = Assert.Single(report.Errors, error => error.Kind == ExcelImportErrorKind.DuplicateColumn);
            Assert.Equal(2, duplicate.ColumnNumber);
            Assert.Equal(2, report.Errors.Count(error => error.Kind == ExcelImportErrorKind.MissingColumn));
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void EmptyHeaderProducesMissingRequiredReportRatherThanArrayPoolFailure()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        sheet.CreateRow(0);
        using var input = Serialize(workbook);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            Assert.Equal(3, report.Errors.Count);
            Assert.All(report.Errors, error => Assert.Equal(ExcelImportErrorKind.MissingColumn, error.Kind));
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void DuplicateModelColumnAliasesAreTemplateErrorsWhileLegacyMappingIsPreserved()
    {
        using var input = Workbook(("ID", "unused", "unused2", "unused3"), [7]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<AliasedColumnsRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            var error = Assert.Single(report.Errors);
            Assert.Equal(ExcelImportErrorKind.DuplicateColumn, error.Kind);
            Assert.Equal(1, error.RowNumber);
            Assert.Equal(1, error.ColumnNumber);
            Assert.Equal("ID", error.ColumnName);
        }
        finally { helper.Workbook?.Dispose(); }
        var legacy = CreateHelper();
        try
        {
            var row = Assert.Single(legacy.Import<AliasedColumnsRow>("rows.xlsx", input));
            Assert.Equal(0, row.First);
            Assert.Equal(7, row.Second);
        }
        finally { legacy.Workbook?.Dispose(); }
    }

    [Fact]
    public void ModelColumnAliasComparisonMatchesExistingOrdinalHeaderMapping()
    {
        using var input = Workbook(("ID", "id", "unused", "unused2"), [1, 2]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<CaseDistinctColumnsRow>("rows.xlsx", input);
            Assert.True(report.IsValid);
            var row = Assert.Single(report.Rows).Value;
            Assert.Equal(1, row.First);
            Assert.Equal(2, row.Second);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void BusinessValidationOnlyReceivesCellValidRowsAndImporterAssignsLocation()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"),
            ["bad", "invalid", true], [1, "business error", true], [2, "valid", false]);
        var calls = new List<int>();
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input, value =>
            {
                calls.Add(value.Id);
                return value.Id == 1 ? [new("ID must exceed one", nameof(ImportRow.Id)), new("Row invalid")] : [];
            });
            Assert.Equal([1, 2], calls);
            Assert.Equal(4, Assert.Single(report.Rows).RowNumber);
            var errors = report.Errors.Where(error => error.Kind == ExcelImportErrorKind.BusinessValidation).ToArray();
            Assert.Equal(2, errors.Length);
            Assert.Equal(3, errors[0].RowNumber);
            Assert.Equal(1, errors[0].ColumnNumber);
            Assert.Equal("ID", errors[0].ColumnName);
            Assert.Null(errors[1].ColumnNumber);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericDisplayRoundingCannotMakeFractionalInputValidForIntegerProperty(bool formula)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("ID");
        var cell = sheet.CreateRow(1).CreateCell(0);
        if (formula)
            cell.SetCellFormula("19/10");
        else
            cell.SetCellValue(1.9);
        var style = workbook.CreateCellStyle();
        style.DataFormat = workbook.CreateDataFormat().GetFormat("0");
        cell.CellStyle = style;
        using var input = Serialize(workbook);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<NumberRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            Assert.Equal(ExcelImportErrorKind.Conversion, Assert.Single(report.Errors).Kind);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void FormulaValuesAndErrorsUseTheExistingEvaluator()
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("ID");
        sheet.CreateRow(1).CreateCell(0).SetCellFormula("1+2");
        sheet.CreateRow(2).CreateCell(0).SetCellFormula("1/0");
        sheet.CreateRow(3).CreateCell(0).SetCellErrorValue(FormulaError.VALUE.Code);
        using var input = Serialize(workbook);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<NumberRow>("rows.xlsx", input);
            Assert.Equal(3, Assert.Single(report.Rows).Value.Id);
            Assert.Equal([3, 4], report.Errors.Select(error => error.RowNumber));
            Assert.All(report.Errors, error => Assert.Equal(ExcelImportErrorKind.Conversion, error.Kind));
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void ErrorsCanBeExportedUsingExistingExporter()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), ["bad", "name", true]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ImportRow>("rows.xlsx", input);
            using var output = new MemoryStream();
            new ExcelHelper().Export(report.Errors, "Errors", output);
            Assert.True(output.CanWrite);
            output.Position = 0;
            using var exported = new XSSFWorkbook(output);
            var sheet = exported.GetSheet("Errors");
            var header = sheet.GetRow(0);
            var rowNumberColumn = Enumerable.Range(0, header.LastCellNum)
                .Single(index => header.GetCell(index).StringCellValue == nameof(ExcelImportError.RowNumber));
            Assert.Equal(2, sheet.GetRow(1).GetCell(rowNumberColumn).NumericCellValue);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void LegacyImportStillIgnoresInvalidValuesAndDoesNotEnforceRequired()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), ["bad", " ", false, "bad"]);
        var helper = CreateHelper();
        try
        {
            var row = Assert.Single(helper.Import<ImportRow>("rows.xlsx", input));
            Assert.Equal(0, row.Id);
            Assert.Null(row.Name);
            Assert.Null(row.Quantity);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportAndWorkbookDisposalLeaveCallerInputOpen(bool binaryXls, bool reportMode)
    {
        using IWorkbook workbook = binaryXls ? new HSSFWorkbook() : new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("ID");
        sheet.CreateRow(1).CreateCell(0).SetCellValue(1);
        using var input = Serialize(workbook);
        var helper = CreateHelper();
        var fileName = binaryXls ? "rows.xls" : "rows.xlsx";
        try
        {
            if (reportMode)
                Assert.Single(helper.ImportWithReport<NumberRow>(fileName, input).Rows);
            else
                Assert.Single(helper.Import<NumberRow>(fileName, input));
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
        Assert.True(input.CanRead);
        input.Position = 0;
        Assert.NotEqual(-1, input.ReadByte());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedWorkbookFailureLeavesCallerInputOpen(bool reportMode)
    {
        using var input = new MemoryStream([1, 2, 3, 4]);
        var helper = CreateHelper();
        try
        {
            if (reportMode)
                Assert.ThrowsAny<Exception>(() => helper.ImportWithReport<NumberRow>("broken.xlsx", input));
            else
                Assert.Throws<InvalidOperationException>(() => helper.Import<NumberRow>("broken.xlsx", input));
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void OpeningAndDisposingTemplateWorkbookLeavesCallerInputOpen()
    {
        using var input = Workbook(("ID", "unused", "unused2", "unused3"));
        var builder = CreateHelper().OpenExcel<NumberRow>(input, []);
        try { Assert.True(input.CanRead); }
        finally { builder.Workbook.Dispose(); }
        Assert.True(input.CanRead);
    }

    [Fact]
    public void UnexpectedCallbackAndSetterFailuresPropagate()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), [1, "name", false]);
        var helper = CreateHelper();
        try
        {
            Assert.Throws<NotSupportedException>(() => helper.ImportWithReport<ImportRow>("rows.xlsx", input,
                _ => throw new NotSupportedException("callback")));
        }
        finally { helper.Workbook?.Dispose(); }
        var setterHelper = CreateHelper();
        try
        {
            Assert.Throws<FormatException>(() => setterHelper.ImportWithReport<ThrowingSetterRow>("rows.xlsx", input));
        }
        finally { setterHelper.Workbook?.Dispose(); }
    }

    [Fact]
    public void NonwritableInputColumnsAreTemplateErrors()
    {
        using var input = Workbook(("ID", "unused", "unused2", "unused3"), [1]);
        var helper = CreateHelper();
        try
        {
            var report = helper.ImportWithReport<ReadOnlyRow>("rows.xlsx", input);
            Assert.Empty(report.Rows);
            Assert.Equal(ExcelImportErrorKind.UnwritableProperty, Assert.Single(report.Errors).Kind);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public async Task AsyncNonseekableInputKeepsCallerStreamOpen()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), [1, "name", true]);
        using var nonseekable = new NonSeekableStream(input.ToArray());
        var helper = CreateHelper();
        try
        {
            var report = await helper.ImportWithReportAsync<ImportRow>("rows.xlsx", nonseekable);
            Assert.True(report.IsValid);
            Assert.Single(report.Rows);
            Assert.True(nonseekable.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void CancellationDuringValidationPropagatesAndPublishesNoPartialReport()
    {
        using var input = Workbook(("ID", "名称", "启用", "数量"), [1, "name", true], [2, "next", true]);
        using var cancellation = new CancellationTokenSource();
        var helper = CreateHelper();
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => helper.ImportWithReport<ImportRow>("rows.xlsx", input,
                _ => { cancellation.Cancel(); return []; }, cancellation.Token));
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public void BinaryXlsAndSparseRowsPreserveRealExcelLocations()
    {
        using var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        sheet.CreateRow(2).CreateCell(0).SetCellValue("ID");
        sheet.CreateRow(5).CreateCell(0).SetCellValue("bad");
        sheet.CreateRow(8).CreateCell(0).SetCellValue(4);
        using var input = Serialize(workbook);
        var helper = new ExcelHelper().InitSheetName("Data").InitStartReadRowIndex(2, 3);
        try
        {
            var report = helper.ImportWithReport<NumberRow>("rows.xls", input);
            Assert.Equal(6, Assert.Single(report.Errors).RowNumber);
            Assert.Equal(9, Assert.Single(report.Rows).RowNumber);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    private sealed class InitializedRow
    {
        [ExcelColumn("名称", Required = true)]
        public string Name { get; set; } = "initializer";
    }
    private sealed class NumberRow
    {
        [ExcelColumn("ID")]
        public int Id { get; set; }
    }
    private sealed class AliasedColumnsRow
    {
        [ExcelColumn("ID")]
        public int First { get; set; }
        [ExcelColumn("ID")]
        public int Second { get; set; }
    }
    private sealed class CaseDistinctColumnsRow
    {
        [ExcelColumn("ID")]
        public int First { get; set; }
        [ExcelColumn("id")]
        public int Second { get; set; }
    }
    private sealed class ReadOnlyRow
    {
        [ExcelColumn("ID")]
        public int Id => 0;
    }
    private sealed class ThrowingSetterRow
    {
        [ExcelColumn("ID")]
        public int Id { get => 0; set => throw new FormatException("setter"); }
    }
    private static ExcelHelper CreateHelper() => new ExcelHelper().InitSheetName("Data").InitStartReadRowIndex(0, 1);

    private static MemoryStream Workbook((string, string, string, string) headers, params object?[][] rows)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        var header = sheet.CreateRow(0);
        var names = new[] { headers.Item1, headers.Item2, headers.Item3, headers.Item4 };
        for (var column = 0; column < names.Length; column++)
            header.CreateCell(column).SetCellValue(names[column]);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = sheet.CreateRow(index + 1);
            for (var column = 0; column < rows[index].Length; column++)
            {
                var value = rows[index][column];
                if (value is null) continue;
                var cell = row.CreateCell(column);
                if (value is bool boolean) cell.SetCellValue(boolean);
                else if (value is int number) cell.SetCellValue(number);
                else cell.SetCellValue((string)value);
            }
        }
        return Serialize(workbook);
    }

    private static MemoryStream Serialize(IWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
