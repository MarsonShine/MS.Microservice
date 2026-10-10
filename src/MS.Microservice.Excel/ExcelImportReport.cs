namespace MS.Microservice.Infrastructure.Utils.Excel;

public enum ExcelImportErrorKind
{
    MissingColumn,
    DuplicateColumn,
    UnwritableProperty,
    Required,
    Conversion,
    BusinessValidation
}

/// <summary>A business error; the importer supplies the actual source row and column.</summary>
public sealed record ExcelRowValidationError(string Message, string? PropertyName = null);

/// <summary>A valid value and its original, one-based Excel row number.</summary>
public sealed record ExcelImportRow<T>(int RowNumber, T Value);

/// <summary>A flat error model that can be exported using ExcelHelper.Export.</summary>
public sealed class ExcelImportError
{
    public string SheetName { get; init; } = "";
    public int RowNumber { get; init; }
    public int? ColumnNumber { get; init; }
    public string? ColumnName { get; init; }
    public string? PropertyName { get; init; }
    public ExcelImportErrorKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string? RawValue { get; init; }
}

public sealed record ExcelImportReport<T>(
    IReadOnlyList<ExcelImportRow<T>> Rows,
    IReadOnlyList<ExcelImportError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
