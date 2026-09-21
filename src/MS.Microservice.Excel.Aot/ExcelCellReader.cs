using System.Globalization;
using System.Numerics;
using NPOI.SS.UserModel;

namespace MS.Microservice.Excel.Aot;

/// <summary>Reads one cell or evaluated formula without boxing model values.</summary>
public readonly struct ExcelCellReader
{
    private readonly ICell cell;
    private readonly DataFormatter formatter;
    private readonly IFormulaEvaluator evaluator;
    private readonly CellValue? evaluated;

    public ExcelCellReader(ICell cell, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        this.cell = cell;
        this.formatter = formatter;
        this.evaluator = evaluator;
        evaluated = null;
        if (cell.CellType == CellType.Formula)
        {
            try { evaluated = evaluator.Evaluate(cell); }
            catch { /* Preserve the formatter fallback for formulas NPOI cannot evaluate directly. */ }
        }
    }

    private CellType Kind => evaluated?.CellType ?? cell.CellType;
    private double Number => evaluated?.NumberValue ?? cell.NumericCellValue;

    public string GetText() => Kind switch
    {
        CellType.String => evaluated?.StringValue ?? cell.StringCellValue,
        CellType.Numeric => Number.ToString(CultureInfo.InvariantCulture),
        CellType.Boolean => (evaluated?.BooleanValue ?? cell.BooleanCellValue).ToString(),
        _ => formatter.FormatCellValue(cell, evaluator)
    };

    public string GetFormattedText() => formatter.FormatCellValue(cell, evaluator);

    public bool TryReadString(out string value)
    {
        value = GetText();
        return !string.IsNullOrWhiteSpace(value);
    }

    public bool TryReadNumber<T>(NumberStyles styles, out T value) where T : struct, INumber<T>
    {
        if (Kind == CellType.Numeric)
        {
            var number = Number;
            if (styles != NumberStyles.Integer || double.IsInteger(number))
            {
                try { value = T.CreateChecked(number); return true; }
                catch (OverflowException) { }
            }
        }
        return T.TryParse(GetText(), styles, CultureInfo.InvariantCulture, out value);
    }

    public bool TryReadBoolean(out bool value)
    {
        if (Kind == CellType.Boolean)
        {
            value = evaluated?.BooleanValue ?? cell.BooleanCellValue;
            return true;
        }
        return bool.TryParse(GetText(), out value);
    }

    public bool TryReadGuid(out Guid value) => Guid.TryParse(GetText(), out value);

    public bool TryReadDateTime(out DateTime value)
    {
        if (cell.CellType == CellType.Numeric)
        {
            if (DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue is { } date)
            {
                value = date;
                return true;
            }
            if (DateUtil.IsValidExcelDate(cell.NumericCellValue))
            {
                value = DateUtil.GetJavaDate(cell.NumericCellValue);
                return true;
            }
        }
        // Date formulas retain their workbook format; a raw serial number is not a date string.
        var text = cell.CellType == CellType.Formula ? GetFormattedText() : GetText();
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            || DateTime.TryParse(text, out value);
    }
}
