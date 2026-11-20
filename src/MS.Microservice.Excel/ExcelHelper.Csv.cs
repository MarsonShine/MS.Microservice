using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MS.Microservice.Infrastructure.Utils.Excel;

namespace MS.Microservice.Infrastructure.Utils;

public partial class ExcelHelper
{
    /// <summary>Reads a header-first CSV using the same model mapping and validation as Excel reports.</summary>
    /// <remarks>Leaves input open. Report row numbers are logical CSV records; SheetName is CSV.
    /// MaximumRows bounds data records, including invalid rows. Sheet/row initialization applies only to Excel.</remarks>
    public Task<ExcelImportReport<T>> ImportCsvWithReportAsync<T>(Stream source,
        Func<T, IEnumerable<ExcelRowValidationError>>? validateRow = null,
        CsvOptions? options = null, int maximumRows = 10000, CancellationToken cancellationToken = default)
        => ImportCsvReportCoreAsync(source, validateRow, false, null, options, maximumRows, cancellationToken);

    /// <summary>Imports CSV rows and asynchronously validates their data annotations.</summary>
    public Task<ExcelImportReport<T>> ImportCsvWithDataAnnotationsAsync<T>(Stream source,
        IServiceProvider? services = null, CsvOptions? options = null, int maximumRows = 10000,
        CancellationToken cancellationToken = default)
        => ImportCsvReportCoreAsync<T>(source, null, true, services, options, maximumRows, cancellationToken);

    private async Task<ExcelImportReport<T>> ImportCsvReportCoreAsync<T>(Stream source,
        Func<T, IEnumerable<ExcelRowValidationError>>? validateRow, bool validateAnnotations,
        IServiceProvider? services, CsvOptions? options, int maximumRows, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        cancellationToken.ThrowIfCancellationRequested();
        var meta = GetOrCreateTypeMeta(typeof(T));
        await using var records = CsvHelper.ReadAsync(source, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        if (!await records.MoveNextAsync().ConfigureAwait(false))
            throw new InvalidDataException("CSV 必须包含表头。");
        var headers = records.Current.Fields;
        var map = headers.Select(name => meta.NameToSlotIndex.TryGetValue(name.Trim(), out var index) ? index : -1).ToArray();
        var errors = ValidateReportColumns(meta, map, map.Length, "CSV", 0);
        var rows = new List<ExcelImportRow<T>>();
        if (errors.Count != 0) return new(rows.AsReadOnly(), errors.AsReadOnly());
        List<ValidationResult>? validationResults = validateAnnotations ? [] : null;

        while (await records.MoveNextAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records.Current;
            if (record.RowNumber - 1 > maximumRows || record.RowNumber > int.MaxValue)
                throw new InvalidDataException("CSV 数据记录数超过 maximumRows。");
            if (record.Fields.Count != headers.Count)
                throw new CsvFormatException("字段数必须与表头一致。", record.RowNumber,
                    Math.Min(record.Fields.Count, headers.Count) + 1);
            var rowIndex = (int)record.RowNumber - 1;
            var value = (T)meta.Factory();
            var errorCount = errors.Count;
            for (var column = 0; column < map.Length; column++)
            {
                if (map[column] < 0) continue;
                var slot = meta.Slots[map[column]];
                var text = record.Fields[column];
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (slot.Required)
                        errors.Add(CreateTableImportError("CSV", ExcelImportErrorKind.Required, rowIndex, column,
                            slot, "必填值不能为空。", text));
                }
                else if (TryConvertValue(text, slot.TargetTypeCode, slot.TargetType, out var converted))
                {
                    // User setters are application code; their exceptions must propagate.
                    slot.Setter(value!, converted);
                }
                else
                    errors.Add(CreateTableImportError("CSV", ExcelImportErrorKind.Conversion, rowIndex, column,
                        slot, $"无法转换为 {slot.TargetType.Name}。", text));
            }
            if (errors.Count != errorCount) continue;
            if (validateRow is not null)
                ValidateBusinessRow(value, validateRow, meta, map, map.Length, rowIndex, "CSV", errors, cancellationToken);
            if (validationResults is not null)
                await ValidateAnnotationsRowAsync(value, services, validationResults, meta, map, map.Length, rowIndex, "CSV", errors, cancellationToken)
                    .ConfigureAwait(false);
            if (errors.Count == errorCount) rows.Add(new((int)record.RowNumber, value));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(rows.AsReadOnly(), errors.AsReadOnly());
    }

    /// <summary>Writes model columns and rows as CSV, reusing ExcelColumn names, order and ignore settings.</summary>
    /// <remarks>Leaves output open. Values use invariant formatting; DateTime uses the round-trip format.
    /// Like CsvHelper, this preserves field text and does not sanitize spreadsheet formulas.</remarks>
    public Task ExportCsvAsync<T>(IEnumerable<T> source, Stream destination, CsvOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var meta = GetOrCreateTypeMeta(typeof(T));
        return CsvHelper.WriteAsync(destination, Records(), options, cancellationToken);

        IEnumerable<IReadOnlyList<string?>> Records()
        {
            // WriteAsync finishes the yielded row before asking this private iterator for the next one.
            var fields = new string?[meta.Slots.Length];
            for (int i = 0; i < fields.Length; i++) fields[i] = meta.Slots[i].ColumnName;
            yield return fields;
            foreach (var row in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(row);
                for (int i = 0; i < fields.Length; i++)
                {
                    fields[i] = meta.Slots[i].Getter(row!) switch
                    {
                        null => null,
                        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                        object value => value.ToString()
                    };
                }
                yield return fields;
            }
        }
    }
}
