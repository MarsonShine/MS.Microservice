using System.ComponentModel.DataAnnotations;
using MS.Microservice.Infrastructure.Utils.Excel;

namespace MS.Microservice.Infrastructure.Utils;

public partial class ExcelHelper
{
    /// <summary>Imports workbook rows and asynchronously validates their data annotations.</summary>
    /// <remarks>The caller owns the input stream, workbook and service provider.</remarks>
    public async ValueTask<ExcelImportReport<T>> ImportWithDataAnnotationsAsync<T>(string fileName, Stream stream,
        IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanSeek)
        {
            using var buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
            return await ImportWithDataAnnotationsAsync<T>(fileName, buffered, services, cancellationToken).ConfigureAwait(false);
        }

        var parsed = ImportCore<T, (ExcelTypeMeta Meta, int[] Map, string Source, List<ExcelImportRow<T>> Rows, List<ExcelImportError> Errors)>(
            fileName, stream, (meta, map, count, formatter, evaluator) =>
            {
                var rows = new List<ExcelImportRow<T>>();
                var source = sheet!.SheetName;
                var errors = ValidateReportColumns(meta, map, count, source, titleRowIndex);
                if (errors.Count == 0)
                    ReadRows<T>(meta, map, count, formatter, evaluator,
                        (number, value) => rows.Add(new(number, value)), errors, cancellationToken: cancellationToken);
                // ImportCore returns its pooled mapping before asynchronous validation begins.
                return (meta, map.AsSpan(0, count).ToArray(), source, rows, errors);
            }, wrapErrors: false);

        var accepted = new List<ExcelImportRow<T>>(parsed.Rows.Count);
        var validationResults = new List<ValidationResult>();
        foreach (var row in parsed.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = parsed.Errors.Count;
            await ValidateAnnotationsRowAsync(row.Value, services, validationResults, parsed.Meta, parsed.Map, parsed.Map.Length,
                row.RowNumber - 1, parsed.Source, parsed.Errors, cancellationToken).ConfigureAwait(false);
            if (parsed.Errors.Count == count) accepted.Add(row);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Parsing and asynchronous validation are separate phases; retain source-row order.
        var errorsInRowOrder = parsed.Errors.OrderBy(error => error.RowNumber).ToList();
        return new(accepted.AsReadOnly(), errorsInRowOrder.AsReadOnly());
    }

    private static async Task ValidateAnnotationsRowAsync<T>(T value, IServiceProvider? services, List<ValidationResult> results,
        ExcelTypeMeta meta, int[] map, int count, int row, string sourceName,
        List<ExcelImportError> errors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new ValidationContext(value!, services, items: null);
        // Rows are awaited in sequence. Findings from the previous row have already been consumed.
        results.Clear();
        await Validator.TryValidateObjectAsync(value!, context, results, validateAllProperties: true, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var result in results)
        {
            foreach (var member in result.MemberNames.DefaultIfEmpty(null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                errors.Add(CreateBusinessError(meta, map, count, row, sourceName,
                    member, result.ErrorMessage ?? "Validation failed."));
            }
        }
    }
}
