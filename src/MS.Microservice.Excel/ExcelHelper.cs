using MS.Microservice.Core.Reflection;
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

public partial class ExcelHelper : IExcelImport, IExcelExport, IAsyncExcelImport, IAsyncExcelExport
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

    private int[] RentColumnIndexMap(ExcelTypeMeta meta, out int columnCount, bool allowEmptyHeader = false)
    {
        var titleRow = sheet?.GetRow(titleRowIndex) ?? throw new InvalidOperationException("未找到标题行");
        columnCount = titleRow.LastCellNum;
        if (allowEmptyHeader)
            columnCount = Math.Max(columnCount, 0);
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
        var list = new List<T>(Math.Max(sheet!.LastRowNum - contentRowIndex + 1, 0));
        ReadRows<T>(meta, columnIndexMap, columnCount, formatter, evaluator, (_, value) => list.Add(value));
        return list;
    }

    private void ReadRows<T>(ExcelTypeMeta meta, int[] columnIndexMap, int columnCount,
        DataFormatter formatter, IFormulaEvaluator evaluator, Action<int, T> acceptRow,
        List<ExcelImportError>? errors = null, Func<T, IEnumerable<ExcelRowValidationError>>? validateRow = null,
        CancellationToken cancellationToken = default)
    {
        var targetSheet = sheet ?? throw new InvalidOperationException("未找到工作表");
        var slots = meta.Slots;
        var factory = meta.Factory;
        for (int i = contentRowIndex; i <= targetSheet.LastRowNum; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IRow? row = targetSheet.GetRow(i);
            if (row == null)
            {
                continue;
            }

            T obj = (T)factory();
            var errorCount = errors?.Count ?? 0;
            for (int j = 0; j < columnCount; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var propertyLocation = columnIndexMap[j];
                if (propertyLocation == -1)
                {
                    continue;
                }

                ICell? cell = row.GetCell(j);
                var slot = slots[propertyLocation];
                object? value = null;
                CellReadStatus status;
                try
                {
                    status = ReadCellValue(cell, slot, formatter, evaluator, errors is not null, out value);
                }
                catch (Exception ex) when (errors is not null && ex is FormatException or OverflowException)
                {
                    status = CellReadStatus.Invalid;
                }
                if (status == CellReadStatus.Success)
                {
                    // Keep user setters outside conversion-error handling.
                    slot.Setter(obj!, value);
                }
                else if (errors is not null && (status == CellReadStatus.Invalid || slot.Required))
                {
                    var kind = status == CellReadStatus.Invalid ? ExcelImportErrorKind.Conversion : ExcelImportErrorKind.Required;
                    var message = kind == ExcelImportErrorKind.Required ? "必填值不能为空。" : $"无法转换为 {slot.TargetType.Name}。";
                    errors.Add(CreateImportError(kind, i, j, slot, message, cell?.ToString()));
                }
            }

            if (errors is not null && errors.Count != errorCount)
                continue;
            if (validateRow is not null)
                ValidateBusinessRow(obj, validateRow, meta, columnIndexMap, columnCount, i,
                    targetSheet.SheetName, errors!, cancellationToken);
            if (errors is null || errors.Count == errorCount)
                acceptRow(i + 1, obj);
        }
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
        => ImportCore<T, List<T>>(fileName, stream, ReadBody<T>);

    /// <summary>Collects located data errors while preserving the original row numbers.</summary>
    public ExcelImportReport<T> ImportWithReport<T>(string fileName, Stream stream,
        Func<T, IEnumerable<ExcelRowValidationError>>? validateRow = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ImportCore<T, ExcelImportReport<T>>(fileName, stream, (meta, map, count, formatter, evaluator) =>
        {
            var rows = new List<ExcelImportRow<T>>();
            var errors = ValidateReportColumns(meta, map, count, sheet!.SheetName, titleRowIndex);
            if (errors.Count == 0)
                ReadRows<T>(meta, map, count, formatter, evaluator,
                    (number, value) => rows.Add(new(number, value)), errors, validateRow, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(rows.AsReadOnly(), errors.AsReadOnly());
        }, wrapErrors: false);
    }

    public async ValueTask<ExcelImportReport<T>> ImportWithReportAsync<T>(string fileName, Stream stream,
        Func<T, IEnumerable<ExcelRowValidationError>>? validateRow = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.CanSeek)
            return ImportWithReport(fileName, stream, validateRow, cancellationToken);
        using var buffered = new MemoryStream();
        await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        return ImportWithReport(fileName, buffered, validateRow, cancellationToken);
    }

    private TResult ImportCore<T, TResult>(string fileName, Stream stream,
        Func<ExcelTypeMeta, int[], int, DataFormatter, IFormulaEvaluator, TResult> readRows, bool wrapErrors = true)
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
            columnIndexMap = RentColumnIndexMap(meta, out int columnCount, allowEmptyHeader: !wrapErrors);

            BeginDiagnosticPhase("import-row-read");
            return readRows(meta, columnIndexMap, columnCount, formatter, evaluator);
        }
        catch (Exception ex) when (wrapErrors && ex is not InvalidOperationException && ex is not ArgumentNullException)
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

        IWorkbook currentWorkbook = WorkbookFactory.Create(new LeaveOpenReadStream(fileStream));
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
        var input = new LeaveOpenReadStream(stream);
        return fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(input)
            : new XSSFWorkbook(input);
    }

    // NPOI closes supplied input streams while loading a workbook; their ownership stays with callers.
    private sealed class LeaveOpenReadStream(Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => source.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
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
            // 属性的元数据发现与读取委托统一由 PropertyAccessors 提供，本方法只负责列名与排序语义。
            var slots = PropertyAccessors.Get(currentType)
                .Select(accessor => new
                {
                    Accessor = accessor,
                    Attribute = accessor.Property.GetCustomAttribute<ExcelColumnAttribute>(inherit: false)
                })
                .Where(item => item.Attribute?.Ignore != true)
                .OrderBy(item => item.Attribute?.Order ?? int.MaxValue)
                .ThenBy(item => item.Accessor.Property.MetadataToken)
                .Select(item =>
                {
                    var targetType = Nullable.GetUnderlyingType(item.Accessor.PropertyType) ?? item.Accessor.PropertyType;
                    var columnName = item.Attribute?.Name?.Trim() ?? item.Accessor.Name;
                    return new ExcelPropertySlot(
                        columnName,
                        item.Accessor.GetValue!,
                        item.Accessor.SetValue ?? SkipWrite,
                        targetType,
                        Type.GetTypeCode(targetType),
                        item.Accessor.Name,
                        item.Attribute?.Required == true,
                        item.Accessor.SetValue is not null);
                })
                .ToArray();

            var nameToSlotIndex = new Dictionary<string, int>(slots.Length, StringComparer.Ordinal);
            for (int i = 0; i < slots.Length; i++)
            {
                nameToSlotIndex[slots[i].ColumnName] = i;
            }

            // 读取路径需要逐行 new T()，没有公开无参构造函数的类型在这里就报错，而不是等 factory() 抛空引用。
            var factory = PropertyAccessors.Factory(currentType)
                ?? throw new InvalidOperationException($"{currentType.FullName} 没有公开无参构造函数，无法作为 Excel 读取模型。");

            return new ExcelTypeMeta(factory, slots, nameToSlotIndex);
        });
    }

    /// <summary>属性不可写（如 <c>init</c> 或只读计算属性）时占位，保持写入路径无分支。</summary>
    private static readonly Action<object, object?> SkipWrite = static (_, _) => { };

    private enum CellReadStatus { Empty, Success, Invalid }

    private static CellReadStatus ReadCellValue(ICell? cell, ExcelPropertySlot slot,
        DataFormatter formatter, IFormulaEvaluator evaluator, bool strict, out object? value)
    {
        value = null;
        if (cell is null)
            return CellReadStatus.Empty;
        if (TryReadCellDirectly(cell, slot.TargetTypeCode, slot.TargetType, evaluator, out value))
            return CellReadStatus.Success;

        var evaluated = strict && cell.CellType == CellType.Formula ? evaluator.Evaluate(cell) : null;
        if (strict && (cell.CellType == CellType.Error || evaluated?.CellType == CellType.Error))
            return CellReadStatus.Invalid;

        string text;
        if (strict && slot.TargetTypeCode is not (TypeCode.DateTime or TypeCode.String) &&
            (cell.CellType == CellType.Numeric || evaluated?.CellType == CellType.Numeric))
            text = (evaluated?.NumberValue ?? cell.NumericCellValue).ToString("R", CultureInfo.InvariantCulture);
        else
            text = formatter.FormatCellValue(cell, evaluator);
        if (string.IsNullOrWhiteSpace(text))
            return CellReadStatus.Empty;
        return TryConvertValue(text, slot.TargetTypeCode, slot.TargetType, out value)
            ? CellReadStatus.Success : CellReadStatus.Invalid;
    }

    private static List<ExcelImportError> ValidateReportColumns(ExcelTypeMeta meta, int[] map, int count,
        string sourceName, int headerRowIndex)
    {
        var errors = new List<ExcelImportError>();
        var comparer = meta.NameToSlotIndex.Comparer;
        foreach (var group in meta.Slots.GroupBy(slot => slot.ColumnName, comparer).Where(group => group.Skip(1).Any()))
        {
            int? column = null;
            for (var index = 0; index < count; index++)
                if (map[index] >= 0 && comparer.Equals(meta.Slots[map[index]].ColumnName, group.Key))
                { column = index; break; }
            errors.Add(CreateTableImportError(sourceName, ExcelImportErrorKind.DuplicateColumn, headerRowIndex, column,
                group.First(), "多个模型属性使用相同列名，无法唯一映射。"));
        }
        var mapped = new HashSet<int>();
        for (var column = 0; column < count; column++)
        {
            if (map[column] < 0)
                continue;
            var slot = meta.Slots[map[column]];
            if (!mapped.Add(map[column]))
                errors.Add(CreateTableImportError(sourceName, ExcelImportErrorKind.DuplicateColumn, headerRowIndex, column, slot, "标题重复映射到同一属性。"));
            if (!slot.CanWrite)
                errors.Add(CreateTableImportError(sourceName, ExcelImportErrorKind.UnwritableProperty, headerRowIndex, column, slot, "导入属性必须可写。"));
        }
        for (var index = 0; index < meta.Slots.Length; index++)
            if (meta.Slots[index].Required && !mapped.Contains(index))
                errors.Add(CreateTableImportError(sourceName, ExcelImportErrorKind.MissingColumn, headerRowIndex, null, meta.Slots[index], "缺少必填列。"));
        return errors;
    }

    private ExcelImportError CreateImportError(ExcelImportErrorKind kind, int row, int? column,
        ExcelPropertySlot? slot, string message, string? rawValue = null, string? propertyName = null)
        => CreateTableImportError(sheet!.SheetName, kind, row, column, slot, message, rawValue, propertyName);

    private static ExcelImportError CreateTableImportError(string sourceName, ExcelImportErrorKind kind, int row, int? column,
        ExcelPropertySlot? slot, string message, string? rawValue = null, string? propertyName = null) => new()
    {
        SheetName = sourceName,
        RowNumber = row + 1,
        ColumnNumber = column + 1,
        ColumnName = slot?.ColumnName,
        PropertyName = propertyName ?? slot?.PropertyName,
        Kind = kind,
        Message = message,
        RawValue = rawValue
    };

    private static void ValidateBusinessRow<T>(T value, Func<T, IEnumerable<ExcelRowValidationError>> validateRow,
        ExcelTypeMeta meta, int[] map, int count, int row, string sourceName,
        List<ExcelImportError> errors, CancellationToken cancellationToken)
    {
        var validationErrors = validateRow(value)
            ?? throw new InvalidOperationException("行校验回调不能返回 null。");
        foreach (var error in validationErrors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(error);
            var slotIndex = Array.FindIndex(meta.Slots, slot => slot.PropertyName == error.PropertyName);
            int? column = null;
            for (var j = 0; j < count; j++)
                if (map[j] == slotIndex && slotIndex >= 0) { column = j; break; }
            errors.Add(CreateTableImportError(sourceName, ExcelImportErrorKind.BusinessValidation, row, column,
                slotIndex < 0 ? null : meta.Slots[slotIndex], error.Message, propertyName: error.PropertyName));
        }
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

    private sealed record ExcelPropertySlot(string ColumnName, Func<object, object?> Getter,
        Action<object, object?> Setter, Type TargetType, TypeCode TargetTypeCode,
        string PropertyName, bool Required, bool CanWrite);

    private sealed record ExcelTypeMeta(
        Func<object> Factory,
        ExcelPropertySlot[] Slots,
        Dictionary<string, int> NameToSlotIndex);
}
