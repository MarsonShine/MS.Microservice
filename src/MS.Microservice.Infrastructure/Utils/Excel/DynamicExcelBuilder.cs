using NPOI.SS.UserModel;
using System.IO.Pipelines;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils.Excel;

public class DynamicExcelBuilder<T>(IWorkbook workbook, ISheet sheetAt, IReadOnlyList<T> source, int storedTitleRowIndex = -1)
{
    private readonly IWorkbook _workbook = workbook;
    private readonly ISheet _sheet = sheetAt;
    private readonly IReadOnlyList<T> _items = source;
    private readonly PropertyInfo[] _properties = typeof(T).GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    private readonly Dictionary<string, int> _columnMapping = [];
    private ColumnBinding[] _bindings = [];
    private readonly int _storedTitleRowIndex = storedTitleRowIndex;
    private bool _useTextCells = false;

    /// <summary>Switches to writing all cell values as strings.</summary>
    public DynamicExcelBuilder<T> UseTextCells() { _useTextCells = true; return this; }

    /// <summary>Writes cell values using their native types (default behaviour).</summary>
    public DynamicExcelBuilder<T> UseTypedCells() { _useTextCells = false; return this; }

    /// <summary>Single-arg overload that uses the title row index supplied when constructing this builder via <c>OpenExcel(..., titleRowIndex)</c>.</summary>
    public DynamicExcelBuilder<T> InitInsertRow(int startRowIndex)
    {
        if (_storedTitleRowIndex < 0)
            throw new InvalidOperationException("titleRowIndex は OpenExcel に渡すか、InitInsertRow(titleRowIndex, startRowIndex) を使ってください。");
        return InitInsertRow(_storedTitleRowIndex, startRowIndex);
    }

    public DynamicExcelBuilder<T> InitInsertRow(int titleRowIndex, int startRowIndex)
    {
        IRow titleRow = _sheet.GetRow(titleRowIndex) ?? throw new InvalidOperationException("未找到标题行");
        ReadTitle(titleRow);

        if (_items.Count == 0)
            return this;

        int itemCount = _items.Count;
        _sheet.ShiftRows(startRowIndex, _sheet.LastRowNum + itemCount + 1, itemCount);

        for (int i = startRowIndex; i < startRowIndex + _items.Count; i++)
        {
            IRow contentRow = _sheet.CreateRow(i);
            contentRow.Height = titleRow.Height;

            foreach (ICell cell in titleRow.Cells)
            {
                ICell contentCell = contentRow.CreateCell(cell.ColumnIndex);
                contentCell.CellStyle = cell.CellStyle;
            }
        }
        return this;
    }

    private void ReadTitle(IRow titleRow)
    {
        _columnMapping.Clear();
        var titles = titleRow.Cells;
        if (titles.Count == 0)
        {
            for (int columnIndex = 0; columnIndex < _properties.Length; columnIndex++)
            {
                ExcelColumnAttribute? attribute = _properties[columnIndex].GetCustomAttribute<ExcelColumnAttribute>();
                if (attribute != null)
                {
                    _columnMapping[attribute.Name ?? _properties[columnIndex].Name] = columnIndex;
                }
            }
        }
        else
        {
            for (int i = 0; i < titles.Count; i++)
            {
                string title = titles[i].ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(title))
                {
                    _columnMapping[title] = titles[i].ColumnIndex;
                }
            }
        }

        _bindings = _properties
            .Select(property => new { Property = property, Attribute = property.GetCustomAttribute<ExcelColumnAttribute>() })
            .Where(item => item.Attribute != null)
            .Select(item =>
            {
                string key = item.Attribute!.Name ?? item.Property.Name;
                return _columnMapping.TryGetValue(key, out int columnIndex)
                    ? new ColumnBinding(item.Property, columnIndex)
                    : (ColumnBinding?)null;
            })
            .Where(binding => binding.HasValue)
            .Select(binding => binding!.Value)
            .ToArray();
    }

    public DynamicExcelBuilder<T> InsertCellValue(int startRowIndex)
    {
        if (Items.Count == 0)
        {
            return this;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            int rowIndex = startRowIndex++;
            IRow row = _sheet.GetRow(rowIndex) ?? _sheet.CreateRow(rowIndex);
            T obj = Items[i];
            foreach (ColumnBinding binding in _bindings)
            {
                ICell cell = row.GetCell(binding.ColumnIndex) ?? row.CreateCell(binding.ColumnIndex);
                object? value = binding.Property.GetValue(obj);
                if (value != null)
                {
                    if (_useTextCells)
                        cell.SetCellValue(value.ToString());
                    else
                        SetCellValue(cell, value);
                }
            }
        }
        return this;
    }

    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _workbook.Write(destination, leaveOpen: true);
    }

    public void Write(PipeWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using Stream writerStream = destination.AsStream(leaveOpen: true);
        _workbook.Write(writerStream, leaveOpen: true);
        writerStream.Flush();
    }

    public async ValueTask WriteAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        Write(destination);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(PipeWriter destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        using Stream writerStream = destination.AsStream(leaveOpen: true);
        _workbook.Write(writerStream, leaveOpen: true);
        await writerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]> WriteAsync()
    {
        using MemoryStream stream = new();
        Write(stream);
        return Task.FromResult(stream.ToArray());
    }

    public IWorkbook Workbook { get { return _workbook; } }

    public IReadOnlyList<T> Items { get { return _items; } }

    private static void SetCellValue(ICell cell, object value)
    {
        switch (value)
        {
            case string stringValue:
                cell.SetCellValue(stringValue);
                break;
            case DateTime dateTimeValue:
                cell.SetCellValue(dateTimeValue);
                break;
            case bool boolValue:
                cell.SetCellValue(boolValue);
                break;
            case short shortValue:
                cell.SetCellValue(shortValue);
                break;
            case int intValue:
                cell.SetCellValue(intValue);
                break;
            case long longValue:
                cell.SetCellValue(longValue);
                break;
            case float floatValue:
                cell.SetCellValue(floatValue);
                break;
            case double doubleValue:
                cell.SetCellValue(doubleValue);
                break;
            case decimal decimalValue:
                cell.SetCellValue((double)decimalValue);
                break;
            default:
                cell.SetCellValue(value.ToString());
                break;
        }
    }

    private readonly record struct ColumnBinding(PropertyInfo Property, int ColumnIndex);
}
