using MS.Microservice.Infrastructure.Utils.Diagnostics;
using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Buffers;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MS.Microservice.Infrastructure.Utils;

public class ExcelHelper : IExcelImport, IExcelExport, IAsyncExcelImport, IAsyncExcelExport
{
    private static readonly ConcurrentDictionary<Type, ExcelTypeMeta> TypeMetaCache = new();
    [ThreadStatic]
    private static DataFormatter? threadDataFormatter;
    private IWorkbook? workbook;
    private ISheet? sheet;
    private string? sheetName;
    private int sheetIndex = -1, titleRowIndex = -1, contentRowIndex = -1;

    /// <summary>
    /// Optional diagnostic probe for internal phase-level performance measurement.
    /// Set to a <see cref="PerformanceProbe"/> instance to collect per-phase metrics.
    /// When the FZ_OFFICE_DIAGNOSTICS symbol is not enabled, phase calls are compiled out.
    /// </summary>
    internal PerformanceProbe? DiagnosticProbe { get; set; }

    public IWorkbook? Workbook => workbook;

    public byte[] Export<T>(List<T> source, string sheetName)
    {
        using MemoryStream buffer = new();
        Export((IReadOnlyList<T>)source, sheetName, buffer);
        return buffer.ToArray();
    }

    public void Export<T>(IReadOnlyList<T> source, string sheetName, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
        BeginDiagnosticPhase("export-workbook-write");
        currentWorkbook.Write(destination, leaveOpen: true);
        EndDiagnosticPhase();
    }

    public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
        BeginDiagnosticPhase("export-workbook-write");
        await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, PipeWriter destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
        BeginDiagnosticPhase("export-workbook-write");
        await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
    }

    public byte[] Export(DataTable dt, string sheetName)
    {
        using MemoryStream buffer = new();
        Export(dt, sheetName, buffer);
        return buffer.ToArray();
    }

    public void Export(DataTable dt, string sheetName, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(dt);
        ArgumentNullException.ThrowIfNull(destination);

        using IWorkbook currentWorkbook = CreateWorkbook(dt, sheetName);
        currentWorkbook.Write(destination, leaveOpen: true);
    }

    public async ValueTask ExportAsync(DataTable dt, string sheetName, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dt);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(dt, sheetName);
        BeginDiagnosticPhase("export-workbook-write");
        await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ExportAsync(DataTable dt, string sheetName, PipeWriter destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dt);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(dt, sheetName);
        BeginDiagnosticPhase("export-workbook-write");
        await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
    }

    private static IWorkbook CreateWorkbook<T>(IReadOnlyList<T> source, string sheetName)
    {
        var currentWorkbook = new XSSFWorkbook();
        var currentSheet = currentWorkbook.CreateSheet(sheetName);
        var meta = GetOrCreateTypeMeta(typeof(T));
        var dateStyle = CreateDateCellStyle(currentWorkbook);
        SetExcelTitle(currentSheet, meta);
        SetExcelBody(currentSheet, source, meta, dateStyle);
        return currentWorkbook;
    }

    private static IWorkbook CreateWorkbook(DataTable dt, string sheetName)
    {
        var currentWorkbook = new XSSFWorkbook();
        var currentSheet = currentWorkbook.CreateSheet(sheetName);
        var dateStyle = CreateDateCellStyle(currentWorkbook);
        IRow title = currentSheet.CreateRow(0);
        for (int i = 0; i < dt.Columns.Count; i++)
        {
            title.CreateCell(i).SetCellValue(dt.Columns[i].ColumnName.Trim());
        }

        int columnCount = dt.Columns.Count;
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            var dataRow = dt.Rows[i];
            IRow row = currentSheet.CreateRow(i + 1);
            for (int j = 0; j < columnCount; j++)
            {
                object? value = dataRow[j];
                if (value is null || value == DBNull.Value)
                {
                    continue;
                }

                SetCellValue(row.CreateCell(j), value, dateStyle);
            }
        }

        return currentWorkbook;
    }

    private static void SetExcelTitle(ISheet sheet, ExcelTypeMeta meta)
    {
        var slots = meta.Slots;
        IRow title = sheet.CreateRow(0);
        for (int i = 0; i < slots.Length; i++)
        {
            title.CreateCell(i).SetCellValue(slots[i].ColumnName);
        }
    }

    private static void SetExcelBody<T>(ISheet sheet, IReadOnlyList<T> source, ExcelTypeMeta meta, ICellStyle dateStyle)
    {
        if (source.Count == 0)
        {
            return;
        }

        var slots = meta.Slots;
        for (int i = 0; i < source.Count; i++)
        {
            IRow row = sheet.CreateRow(i + 1);
            var obj = source[i];
            for (int j = 0; j < slots.Length; j++)
            {
                var val = slots[j].Getter(obj!);
                if (val == null)
                {
                    continue;
                }

                SetCellValue(row.CreateCell(j), val, dateStyle);
            }
        }
    }

    public List<T> Import<T>(byte[] data)
    {
        using MemoryStream ms = new(data, writable: false);
        return Import<T>("unknown.xlsx", ms);
    }

    public ValueTask<List<T>> ImportAsync<T>(byte[] data, CancellationToken cancellationToken = default)
    {
        return ImportAsync<T>("unknown.xlsx", data, cancellationToken);
    }

    private int[] RentColumnIndexMap(ExcelTypeMeta meta, out int columnCount)
    {
        var titleRow = sheet?.GetRow(titleRowIndex) ?? throw new InvalidOperationException("未找到标题行");
        columnCount = titleRow.LastCellNum;
        int[] columnIndexMap = ArrayPool<int>.Shared.Rent(columnCount);
        Array.Fill(columnIndexMap, -1, 0, columnCount);

        var nameToIndex = meta.NameToSlotIndex;

        for (int i = 0; i < columnCount; i++)
        {
            ICell? cell = titleRow.GetCell(i);
            if (cell == null)
            {
                continue;
            }

            var excelTitle = cell.CellType == CellType.String
                ? cell.StringCellValue.Trim()
                : cell.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(excelTitle))
            {
                continue;
            }

            if (nameToIndex.TryGetValue(excelTitle, out int propertyIndex))
            {
                columnIndexMap[i] = propertyIndex;
            }
        }

        return columnIndexMap;
    }

    private List<T> ReadBody<T>(ExcelTypeMeta meta, int[] columnIndexMap, int columnCount, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var targetSheet = sheet ?? throw new InvalidOperationException("未找到工作表");
        var slots = meta.Slots;
        var factory = meta.Factory;
        var list = new List<T>(Math.Max(targetSheet.LastRowNum - contentRowIndex + 1, 0));
        for (int i = contentRowIndex; i <= targetSheet.LastRowNum; i++)
        {
            IRow? row = targetSheet.GetRow(i);
            if (row == null)
            {
                continue;
            }

            T obj = (T)factory();
            for (int j = 0; j < columnCount; j++)
            {
                var propertyLocation = columnIndexMap[j];
                if (propertyLocation == -1)
                {
                    continue;
                }

                ICell? cell = row.GetCell(j);
                if (cell == null)
                {
                    continue;
                }

                var slot = slots[propertyLocation];

                // Fast path: try direct cell-type-based reading (avoids string allocation)
                if (TryReadCellDirectly(cell, slot.TargetTypeCode, slot.TargetType, evaluator, out object? directValue))
                {
                    slot.Setter(obj!, directValue);
                    continue;
                }

                // Fallback: DataFormatter string path
                var value = formatter.FormatCellValue(cell, evaluator);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                SetPropertyValue(obj!, slot, value);
            }

            list.Add(obj);
        }

        return list;
    }

    public List<T> Import<T>(string fileName, byte[] data)
    {
        using MemoryStream ms = new(data, writable: false);
        return Import<T>(fileName, ms);
    }

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, byte[] data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream ms = new(data, writable: false);
        return await ImportAsync<T>(fileName, ms, cancellationToken).ConfigureAwait(false);
    }

    public List<T> Import<T>(string fileName, Stream stream)
    {
        int[]? columnIndexMap = null;
        try
        {
            ArgumentNullException.ThrowIfNull(stream);
            PrepareStreamForRead(stream);

            BeginDiagnosticPhase("import-workbook-open");
            workbook = CreateWorkbookForRead(fileName, stream);

            if (titleRowIndex == -1 || contentRowIndex == -1)
            {
                throw new InvalidOperationException($"无效操作：请初始化 {nameof(titleRowIndex)} 与 {nameof(contentRowIndex)}，您在解析文件之前应调用方法 InitStartReadRowIndex");
            }

            BeginDiagnosticPhase("import-sheet-resolve");
            sheet = ResolveSheet(workbook);

            BeginDiagnosticPhase("import-meta-cache");
            var meta = GetOrCreateTypeMeta(typeof(T));

            var formatter = GetThreadDataFormatter();
            var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

            BeginDiagnosticPhase("import-title-map");
            columnIndexMap = RentColumnIndexMap(meta, out int columnCount);

            BeginDiagnosticPhase("import-row-read");
            return ReadBody<T>(meta, columnIndexMap, columnCount, formatter, evaluator);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentNullException)
        {
            throw new InvalidOperationException("模板解析错误，请确认导入的模板格式", ex);
        }
        finally
        {
            if (columnIndexMap != null)
            {
                ArrayPool<int>.Shared.Return(columnIndexMap, clearArray: false);
            }

            EndDiagnosticPhase();
        }
    }

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.CanSeek)
        {
            return Import<T>(fileName, stream);
        }

        BeginDiagnosticPhase("import-stream-buffer-copy");
        using MemoryStream bufferedStream = new();
        await stream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return Import<T>(fileName, bufferedStream);
    }

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, PipeReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("import-pipe-buffer-copy");
        using MemoryStream bufferedStream = new();
        using Stream readerStream = reader.AsStream(leaveOpen: true);
        await readerStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return Import<T>(fileName, bufferedStream);
    }

    public ExcelHelper InitSheetIndex(int sheetIndex)
    {
        this.sheetIndex = sheetIndex;
        sheetName = null;
        return this;
    }

    public ExcelHelper InitSheetName(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        this.sheetName = sheetName;
        sheetIndex = -1;
        return this;
    }

    public ExcelHelper InitStartReadRowIndex(int titleRowIndex, int contentRowIndex)
    {
        this.titleRowIndex = titleRowIndex;
        this.contentRowIndex = contentRowIndex;
        return this;
    }

    private int ResolveSheetIndex(IWorkbook currentWorkbook)
    {
        ArgumentNullException.ThrowIfNull(currentWorkbook);

        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            int namedSheetIndex = currentWorkbook.GetSheetIndex(sheetName);
            if (namedSheetIndex < 0)
            {
                throw new InvalidOperationException($"未找到名称为 {sheetName} 的工作表");
            }

            return namedSheetIndex;
        }

        if (sheetIndex >= 0)
        {
            if (sheetIndex >= currentWorkbook.NumberOfSheets)
            {
                throw new InvalidOperationException($"工作表索引 {sheetIndex} 超出范围");
            }

            return sheetIndex;
        }

        var sheetCount = currentWorkbook.NumberOfSheets;
        if (sheetCount <= 0)
        {
            throw new InvalidOperationException("模板中不存在工作表");
        }

        return sheetCount - 1;
    }

    private ISheet ResolveSheet(IWorkbook currentWorkbook)
    {
        int resolvedSheetIndex = ResolveSheetIndex(currentWorkbook);
        sheetIndex = resolvedSheetIndex;
        sheetName = currentWorkbook.GetSheetName(resolvedSheetIndex);
        return currentWorkbook.GetSheetAt(resolvedSheetIndex);
    }

    public DynamicExcelBuilder<T> OpenExcel<T>(Stream fileStream, List<T> source)
    {
        return OpenExcel(fileStream, (IReadOnlyList<T>)source);
    }

    public DynamicExcelBuilder<T> OpenExcel<T>(Stream fileStream, IReadOnlyList<T> source)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentNullException.ThrowIfNull(source);

        PrepareStreamForRead(fileStream);

        IWorkbook currentWorkbook = WorkbookFactory.Create(fileStream);
        ISheet sheetAt = ResolveSheet(currentWorkbook);
        return new DynamicExcelBuilder<T>(currentWorkbook, sheetAt, source);
    }

    public ValueTask<DynamicExcelBuilder<T>> OpenExcelAsync<T>(Stream fileStream, IReadOnlyList<T> source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OpenExcel(fileStream, source));
    }

    public async ValueTask<DynamicExcelBuilder<T>> OpenExcelAsync<T>(PipeReader reader, IReadOnlyList<T> source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("openexcel-pipe-buffer-copy");
        using MemoryStream bufferedStream = new();
        using Stream readerStream = reader.AsStream(leaveOpen: true);
        await readerStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return OpenExcel(bufferedStream, source);
    }

    public DynamicExcelBuilder<T> OpenExcel<T>(string filePath, List<T> source)
    {
        using Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return OpenExcel(fileStream, source);
    }

    private static void PrepareStreamForRead(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }

    private static IWorkbook CreateWorkbookForRead(string fileName, Stream stream)
    {
        return fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);
    }

    private static async ValueTask WriteWorkbookAsync(IWorkbook currentWorkbook, Stream destination, CancellationToken cancellationToken)
    {
        currentWorkbook.Write(destination, leaveOpen: true);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteWorkbookAsync(IWorkbook currentWorkbook, PipeWriter destination, CancellationToken cancellationToken)
    {
        using Stream writerStream = destination.AsStream(leaveOpen: true);
        currentWorkbook.Write(writerStream, leaveOpen: true);
        await writerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ExcelTypeMeta GetOrCreateTypeMeta(Type type)
    {
        return TypeMetaCache.GetOrAdd(type, static currentType =>
        {
            var properties = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var slots = properties
                .Select(property => new
                {
                    Property = property,
                    Attribute = property.GetCustomAttribute<ExcelColumnAttribute>(inherit: false)
                })
                .Where(item => item.Attribute?.Ignore != true)
                .OrderBy(item => item.Attribute?.Order ?? int.MaxValue)
                .ThenBy(item => item.Property.MetadataToken)
                .Select(item =>
                {
                    var targetType = Nullable.GetUnderlyingType(item.Property.PropertyType) ?? item.Property.PropertyType;
                    var columnName = item.Attribute?.Name?.Trim() ?? item.Property.Name;
                    var getter = ReflectionDelegateFactory.CreateGetter(item.Property);
                    var setter = ReflectionDelegateFactory.CreateSetter(item.Property);
                    var typeCode = Type.GetTypeCode(targetType);
                    return new ExcelPropertySlot(columnName, getter, setter, targetType, typeCode);
                })
                .ToArray();

            var nameToSlotIndex = new Dictionary<string, int>(slots.Length, StringComparer.Ordinal);
            for (int i = 0; i < slots.Length; i++)
            {
                nameToSlotIndex[slots[i].ColumnName] = i;
            }

            var factory = ReflectionDelegateFactory.CreateFactory(currentType);

            return new ExcelTypeMeta(factory, slots, nameToSlotIndex);
        });
    }

    private static void SetPropertyValue<T>(T target, ExcelPropertySlot slot, string value)
    {
        if (!TryConvertValue(value, slot.TargetTypeCode, slot.TargetType, out object? convertedValue))
        {
            return;
        }

        slot.Setter(target!, convertedValue);
    }

    [Conditional("MS_Microservice_DIAGNOSTICS")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BeginDiagnosticPhase(string phaseName)
    {
        DiagnosticProbe?.BeginPhase(phaseName);
    }

    [Conditional("MS_Microservice_DIAGNOSTICS")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndDiagnosticPhase()
    {
        DiagnosticProbe?.EndPhase();
    }

    private static DataFormatter GetThreadDataFormatter()
    {
        return threadDataFormatter ??= new DataFormatter(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Try to read a cell value directly by <see cref="ICell.CellType"/>, avoiding the
    /// <see cref="DataFormatter"/> string round-trip for numeric and boolean cells.
    /// </summary>
    private static bool TryReadCellDirectly(ICell cell, TypeCode targetTypeCode, Type targetType, IFormulaEvaluator evaluator, out object? value)
    {
        value = null;
        var cellType = cell.CellType;

        // Formula cells: evaluate first, then read from the evaluated result
        if (cellType == CellType.Formula)
        {
            try
            {
                var evaluated = evaluator.Evaluate(cell);
                if (evaluated == null) return false;
                return TryReadEvaluatedCellValue(evaluated, targetTypeCode, targetType, out value);
            }
            catch
            {
                return false; // fall back to formatter
            }
        }

        // Non-formula cells: read directly from cell
        return TryReadNonFormulaCellValue(cell, cellType, targetTypeCode, targetType, out value);
    }

    /// <summary>
    /// Read value from a non-formula cell directly by its <see cref="CellType"/>.
    /// </summary>
    private static bool TryReadNonFormulaCellValue(ICell cell, CellType cellType, TypeCode targetTypeCode, Type targetType, out object? value)
    {
        value = null;

        switch (cellType)
        {
            case CellType.Numeric:
                double numericValue = cell.NumericCellValue;
                // DateTime: use NPOI's DateUtil for direct date extraction
                if (targetTypeCode == TypeCode.DateTime || targetType == typeof(DateTime))
                {
                    if (DateUtil.IsCellDateFormatted(cell))
                    {
                        value = cell.DateCellValue;
                        return true;
                    }

                    if (DateUtil.IsValidExcelDate(numericValue))
                    {
                        value = DateUtil.GetJavaDate(numericValue);
                        return true;
                    }

                    return false; // let formatter handle it
                }

                return TryConvertFromDouble(numericValue, targetTypeCode, out value);

            case CellType.String:
                string strValue = cell.StringCellValue;
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    return false;
                }

                if (targetTypeCode == TypeCode.String)
                {
                    value = strValue;
                    return true;
                }
                return TryConvertValue(strValue, targetTypeCode, targetType, out value);

            case CellType.Boolean:
                bool boolValue = cell.BooleanCellValue;
                if (targetTypeCode == TypeCode.Boolean)
                {
                    value = boolValue;
                    return true;
                }
                return TryConvertValue(boolValue.ToString(), targetTypeCode, targetType, out value);

            case CellType.Blank:
                return false;

            default:
                return false; // fall back to formatter
        }
    }

    /// <summary>
    /// Read value from an evaluated formula result (<see cref="NPOI.SS.UserModel.CellValue"/>).
    /// This avoids the inconsistency of checking type from evaluated result but reading from raw cell.
    /// </summary>
    private static bool TryReadEvaluatedCellValue(NPOI.SS.UserModel.CellValue evaluated, TypeCode targetTypeCode, Type targetType, out object? value)
    {
        value = null;

        switch (evaluated.CellType)
        {
            case CellType.Numeric:
                double numericValue = evaluated.NumberValue;
                if (targetTypeCode == TypeCode.DateTime || targetType == typeof(DateTime))
                {
                    // For formula-evaluated dates, we lack cell format info — fall back
                    return false;
                }
                return TryConvertFromDouble(numericValue, targetTypeCode, out value);

            case CellType.String:
                string strValue = evaluated.StringValue;
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    return false;
                }
                if (targetTypeCode == TypeCode.String)
                {
                    value = strValue;
                    return true;
                }
                return TryConvertValue(strValue, targetTypeCode, targetType, out value);

            case CellType.Boolean:
                bool boolValue = evaluated.BooleanValue;
                if (targetTypeCode == TypeCode.Boolean)
                {
                    value = boolValue;
                    return true;
                }
                return TryConvertValue(boolValue.ToString(), targetTypeCode, targetType, out value);

            case CellType.Blank:
            default:
                return false;
        }
    }

    /// <summary>
    /// Convert a raw double from a numeric cell to the target type without string allocation.
    /// For integer types, rejects non-integer values (e.g., 1.9 → fail, not truncate to 1).
    /// </summary>
    private static bool TryConvertFromDouble(double numericValue, TypeCode targetTypeCode, out object? value)
    {
        switch (targetTypeCode)
        {
            case TypeCode.Double:
                value = numericValue;
                return true;
            case TypeCode.Single:
                value = (float)numericValue;
                return true;
            case TypeCode.Decimal:
                value = (decimal)numericValue;
                return true;
            case TypeCode.Int32:
                if (numericValue != Math.Truncate(numericValue))
                {
                    value = null;
                    return false;
                }
                if (numericValue is >= int.MinValue and <= int.MaxValue)
                {
                    value = checked((int)numericValue);
                    return true;
                }
                break;
            case TypeCode.Int64:
                if (numericValue != Math.Truncate(numericValue))
                {
                    value = null;
                    return false;
                }
                if (numericValue is >= long.MinValue and <= long.MaxValue)
                {
                    value = checked((long)numericValue);
                    return true;
                }
                break;
            case TypeCode.Int16:
                if (numericValue != Math.Truncate(numericValue))
                {
                    value = null;
                    return false;
                }
                if (numericValue is >= short.MinValue and <= short.MaxValue)
                {
                    value = checked((short)numericValue);
                    return true;
                }
                break;
            case TypeCode.Byte:
                if (numericValue != Math.Truncate(numericValue))
                {
                    value = null;
                    return false;
                }
                if (numericValue is >= byte.MinValue and <= byte.MaxValue)
                {
                    value = checked((byte)numericValue);
                    return true;
                }
                break;
            case TypeCode.String:
                value = numericValue.ToString(CultureInfo.InvariantCulture);
                return true;
        }

        value = null;
        return false;
    }

    private static bool TryConvertValue(string value, TypeCode typeCode, Type targetType, out object? convertedValue)
    {
        if (typeCode == TypeCode.String)
        {
            convertedValue = value;
            return true;
        }

        if (typeCode == TypeCode.DateTime)
        {
            var parsed = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime)
                || DateTime.TryParse(value, out dateTime);
            convertedValue = parsed ? dateTime : null;
            return parsed;
        }

        // Guid reports TypeCode.Object, handle it before the general switch
        if (targetType == typeof(Guid))
        {
            var parsed = Guid.TryParse(value, out Guid guid);
            convertedValue = parsed ? guid : null;
            return parsed;
        }

        if (targetType.IsEnum)
        {
            try
            {
                convertedValue = Enum.Parse(targetType, value, ignoreCase: true);
                return true;
            }
            catch
            {
                convertedValue = null;
                return false;
            }
        }

        switch (typeCode)
        {
            case TypeCode.Boolean:
                var parsedBoolean = bool.TryParse(value, out bool booleanValue);
                convertedValue = parsedBoolean ? booleanValue : null;
                return parsedBoolean;
            case TypeCode.Byte:
                var parsedByte = byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte byteValue);
                convertedValue = parsedByte ? byteValue : null;
                return parsedByte;
            case TypeCode.Decimal:
                var parsedDecimal = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue);
                convertedValue = parsedDecimal ? decimalValue : null;
                return parsedDecimal;
            case TypeCode.Double:
                var parsedDouble = double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue);
                convertedValue = parsedDouble ? doubleValue : null;
                return parsedDouble;
            case TypeCode.Int16:
                var parsedInt16 = short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out short int16Value);
                convertedValue = parsedInt16 ? int16Value : null;
                return parsedInt16;
            case TypeCode.Int32:
                var parsedInt32 = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int int32Value);
                convertedValue = parsedInt32 ? int32Value : null;
                return parsedInt32;
            case TypeCode.Int64:
                var parsedInt64 = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long int64Value);
                convertedValue = parsedInt64 ? int64Value : null;
                return parsedInt64;
            case TypeCode.Single:
                var parsedSingle = float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float singleValue);
                convertedValue = parsedSingle ? singleValue : null;
                return parsedSingle;
            default:
                convertedValue = null;
                return false;
        }
    }

    private static ICellStyle CreateDateCellStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        var format = workbook.CreateDataFormat();
        style.DataFormat = format.GetFormat("yyyy-mm-dd");
        return style;
    }

    private static void SetCellValue(ICell cell, object value, ICellStyle? dateStyle = null)
    {
        switch (value)
        {
            case string stringValue:
                cell.SetCellValue(stringValue);
                break;
            case DateTime dateTimeValue:
                cell.SetCellValue(dateTimeValue);
                if (dateStyle is not null)
                {
                    cell.CellStyle = dateStyle;
                }
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

    private sealed record ExcelPropertySlot(string ColumnName, Func<object, object?> Getter, Action<object, object?> Setter, Type TargetType, TypeCode TargetTypeCode);

    private sealed record ExcelTypeMeta(
        Func<object> Factory,
        ExcelPropertySlot[] Slots,
        Dictionary<string, int> NameToSlotIndex);
}
