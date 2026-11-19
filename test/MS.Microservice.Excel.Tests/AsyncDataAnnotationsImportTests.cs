using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class AsyncDataAnnotationsImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsOnlyValidRowsAndLocatesAsyncAttributeAndObjectErrors(bool csv)
    {
        var probe = new Probe();
        using var input = Input(csv, ["Name", "Id"],
            ["good", "1"], ["bad", "2"], ["pair", "3"], ["row", "4"], ["invalid", "no-number"], ["last", "5"]);
        var helper = CreateHelper();
        try
        {
            var report = await Import(csv, helper, input, probe);
            Assert.Equal([2, 7], report.Rows.Select(row => row.RowNumber));
            Assert.Equal(["good", "bad", "pair", "row", "last"], probe.Names);
            Assert.Equal([3, 4, 4, 4, 5, 6], report.Errors.Select(error => error.RowNumber));
            Assert.Equal([1, 1, 2, null, null, 2], report.Errors.Select(error => error.ColumnNumber));
            Assert.Equal(["Name", "Name", "Id", "Unknown", null, "Id"], report.Errors.Select(error => error.PropertyName));
            Assert.All(report.Errors.Take(5), error => Assert.Equal(ExcelImportErrorKind.BusinessValidation, error.Kind));
            Assert.Equal(ExcelImportErrorKind.Conversion, report.Errors[5].Kind);
            Assert.All(report.Errors, error => Assert.Equal(csv ? "CSV" : "Data", error.SheetName));
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidHeadersSkipValidation(bool csv)
    {
        var probe = new Probe();
        using var input = Input(csv, ["Name"], ["good"]);
        var helper = CreateHelper();
        try
        {
            var report = await Import(csv, helper, input, probe);
            Assert.Equal(ExcelImportErrorKind.MissingColumn, Assert.Single(report.Errors).Kind);
            Assert.Empty(probe.Names);
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationAndValidatorFailuresPropagateAndLeaveInputOpen(bool csv, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new Probe { Failure = cancel ? null : new InvalidOperationException("validator failed"), Cancel = cancel ? cancellation : null };
        using var input = Input(csv, ["Name", "Id"], ["good", "1"]);
        var helper = CreateHelper();
        try
        {
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Import(csv, helper, input, probe, cancellation.Token));
            else
                Assert.Equal("validator failed", (await Assert.ThrowsAsync<InvalidOperationException>(() => Import(csv, helper, input, probe))).Message);
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingReportEntryPointsDoNotRunDataAnnotations(bool csv)
    {
        using var input = Input(csv, ["Name", "Id"], ["bad", "2"]);
        var helper = CreateHelper();
        try
        {
            var report = csv ? await helper.ImportCsvWithReportAsync<Row>(input)
                : await helper.ImportWithReportAsync<Row>("test.xlsx", input);
            Assert.Single(report.Rows);
            Assert.Empty(report.Errors);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelsWithoutAnnotationsRemainValid(bool csv)
    {
        using var input = Input(csv, ["Id"], ["12"]);
        var helper = CreateHelper();
        try
        {
            var report = csv ? await helper.ImportCsvWithDataAnnotationsAsync<PlainRow>(input)
                : await helper.ImportWithDataAnnotationsAsync<PlainRow>("test.xlsx", input);
            Assert.Equal(12, Assert.Single(report.Rows).Value.Id);
            Assert.Empty(report.Errors);
            Assert.True(input.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Fact]
    public async Task WorkbookSupportsNonSeekableInputWithoutClosingIt()
    {
        using var inner = Input(false, ["Name", "Id"], ["good", "1"]);
        using var input = new NonSeekableStream(inner);
        var probe = new Probe();
        var helper = CreateHelper();
        try
        {
            var report = await Import(false, helper, input, probe);
            Assert.True(report.IsValid);
            Assert.Single(report.Rows);
            Assert.True(input.CanRead);
            Assert.True(inner.CanRead);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultWithoutMemberNameIsAnUnlocatedRowError(bool csv)
    {
        using var input = Input(csv, ["Value"], ["x"]);
        var helper = CreateHelper();
        try
        {
            var report = csv ? await helper.ImportCsvWithDataAnnotationsAsync<UnlocatedRow>(input)
                : await helper.ImportWithDataAnnotationsAsync<UnlocatedRow>("test.xlsx", input);
            var error = Assert.Single(report.Errors);
            Assert.Equal(2, error.RowNumber);
            Assert.Null(error.PropertyName);
            Assert.Null(error.ColumnNumber);
            Assert.Equal("unlocated", error.Message);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    private static ExcelHelper CreateHelper() => new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);

    private static async Task<ExcelImportReport<Row>> Import(bool csv, ExcelHelper helper, Stream input,
        Probe probe, CancellationToken token = default)
        => csv ? await helper.ImportCsvWithDataAnnotationsAsync<Row>(input, probe, cancellationToken: token)
            : await helper.ImportWithDataAnnotationsAsync<Row>("test.xlsx", input, probe, token);

    private static MemoryStream Input(bool csv, string[] header, params string[][] rows)
    {
        if (csv)
            return new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', new[] { header }.Concat(rows).Select(row => string.Join(',', row)))));
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Data");
        foreach (var (values, rowIndex) in new[] { header }.Concat(rows).Select((values, index) => (values, index)))
        {
            var row = sheet.CreateRow(rowIndex);
            for (var column = 0; column < values.Length; column++) row.CreateCell(column).SetCellValue(values[column]);
        }
        var output = new MemoryStream();
        workbook.Write(output, leaveOpen: true);
        output.Position = 0;
        return output;
    }

    public sealed class Row : IAsyncValidatableObject
    {
        [ExcelColumn("Name", Required = true), RemoteCheck] public string Name { get; set; } = "";
        [ExcelColumn("Id", Required = true)] public int Id { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            => throw new InvalidOperationException("Use asynchronous validation.");

        public async IAsyncEnumerable<ValidationResult> ValidateAsync(ValidationContext validationContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Id == 3) yield return new ValidationResult("pair error", [nameof(Name), nameof(Id), "Unknown"]);
            if (Id == 4) yield return new ValidationResult("row error");
        }
    }

    public sealed class PlainRow
    {
        [ExcelColumn("Id", Required = true)] public int Id { get; set; }
    }

    public sealed class UnlocatedRow
    {
        [ExcelColumn("Value"), Unlocated] public string Value { get; set; } = "";
    }

    private sealed class UnlocatedAttribute : AsyncValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new InvalidOperationException("Use asynchronous validation.");

        protected override Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext,
            CancellationToken cancellationToken) => Task.FromResult<ValidationResult?>(new ValidationResult("unlocated"));
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Probe : IServiceProvider
    {
        public List<string> Names { get; } = [];
        public Exception? Failure { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public object? GetService(Type serviceType) => serviceType == typeof(Probe) ? this : null;
    }

    private sealed class RemoteCheckAttribute : AsyncValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new InvalidOperationException("Use asynchronous validation.");

        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext,
            CancellationToken cancellationToken)
        {
            var probe = (Probe?)validationContext.GetService(typeof(Probe)) ?? throw new InvalidOperationException("Probe is required.");
            await Task.Yield();
            probe.Names.Add((string)value!);
            if (probe.Failure is not null) throw probe.Failure;
            probe.Cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return (string)value! == "bad" ? new ValidationResult("remote error", [validationContext.MemberName!]) : ValidationResult.Success;
        }
    }
}
