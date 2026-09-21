using MS.Microservice.Excel.Aot.Diagnostics;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Buffers;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace MS.Microservice.Excel.Aot;

public class ExcelHelper : IExcelImport, IExcelExport, IAsyncExcelImport, IAsyncExcelExport
{
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

    public byte[] Export<T>(List<T> source, string sheetName, ExcelModelMap<T> map) where T : class
    {
        using MemoryStream buffer = new();
        Export((IReadOnlyList<T>)source, sheetName, buffer, map);
        return buffer.ToArray();
    }

    public void Export<T>(IReadOnlyList<T> source, string sheetName, Stream destination, ExcelModelMap<T> map) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName, map);
        BeginDiagnosticPhase("export-workbook-write");
        currentWorkbook.Write(destination, leaveOpen: true);
        EndDiagnosticPhase();
    }

    public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, Stream destination, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName, map);
        BeginDiagnosticPhase("export-workbook-write");
        await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, PipeWriter destination, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("export-workbook-create");
        using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName, map);
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

    private static IWorkbook CreateWorkbook<T>(IReadOnlyList<T> source, string sheetName, ExcelModelMap<T> map) where T : class
    {
        ArgumentNullException.ThrowIfNull(map);
        var currentWorkbook = new XSSFWorkbook();
        var currentSheet = currentWorkbook.CreateSheet(sheetName);
        var meta = map;
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

    private static void SetExcelTitle<T>(ISheet sheet, ExcelModelMap<T> meta) where T : class
    {
        var slots = meta.Slots;
        IRow title = sheet.CreateRow(0);
        for (int i = 0; i < slots.Length; i++)
        {
            title.CreateCell(i).SetCellValue(slots[i].ColumnName);
        }
    }

    private static void SetExcelBody<T>(ISheet sheet, IReadOnlyList<T> source, ExcelModelMap<T> meta, ICellStyle dateStyle) where T : class
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
                slots[j].Write(obj, row, j, dateStyle);
            }
        }
    }

    public List<T> Import<T>(byte[] data, ExcelModelMap<T> map) where T : class
    {
        using MemoryStream ms = new(data, writable: false);
        return Import<T>("unknown.xlsx", ms, map);
    }

    public ValueTask<List<T>> ImportAsync<T>(byte[] data, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        return ImportAsync<T>("unknown.xlsx", data, map, cancellationToken);
    }

    private int[] RentColumnIndexMap<T>(ExcelModelMap<T> meta, out int columnCount) where T : class
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

    private List<T> ReadBody<T>(ExcelModelMap<T> meta, int[] columnIndexMap, int columnCount, DataFormatter formatter, IFormulaEvaluator evaluator) where T : class
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

            T obj = factory();
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

                if (slot.Read is { } read)
                    read(obj, new ExcelCellReader(cell, formatter, evaluator));
            }

            list.Add(obj);
        }

        return list;
    }

    public List<T> Import<T>(string fileName, byte[] data, ExcelModelMap<T> map) where T : class
    {
        using MemoryStream ms = new(data, writable: false);
        return Import<T>(fileName, ms, map);
    }

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, byte[] data, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream ms = new(data, writable: false);
        return await ImportAsync<T>(fileName, ms, map, cancellationToken).ConfigureAwait(false);
    }

    public List<T> Import<T>(string fileName, Stream stream, ExcelModelMap<T> map) where T : class
    {
        ArgumentNullException.ThrowIfNull(map);
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
            var meta = map;

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

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, Stream stream, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.CanSeek)
        {
            return Import<T>(fileName, stream, map);
        }

        BeginDiagnosticPhase("import-stream-buffer-copy");
        using MemoryStream bufferedStream = new();
        await stream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return Import<T>(fileName, bufferedStream, map);
    }

    public async ValueTask<List<T>> ImportAsync<T>(string fileName, PipeReader reader, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("import-pipe-buffer-copy");
        using MemoryStream bufferedStream = new();
        using Stream readerStream = reader.AsStream(leaveOpen: true);
        await readerStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return Import<T>(fileName, bufferedStream, map);
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

    public DynamicExcelBuilder<T> OpenExcel<T>(Stream fileStream, List<T> source, ExcelModelMap<T> map) where T : class
    {
        return OpenExcel(fileStream, (IReadOnlyList<T>)source, map);
    }

    public DynamicExcelBuilder<T> OpenExcel<T>(Stream fileStream, IReadOnlyList<T> source, ExcelModelMap<T> map) where T : class
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(map);
        PrepareStreamForRead(fileStream);

        IWorkbook currentWorkbook = WorkbookFactory.Create(fileStream);
        ISheet sheetAt = ResolveSheet(currentWorkbook);
        return new DynamicExcelBuilder<T>(currentWorkbook, sheetAt, source, map);
    }

    public ValueTask<DynamicExcelBuilder<T>> OpenExcelAsync<T>(Stream fileStream, IReadOnlyList<T> source, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OpenExcel(fileStream, source, map));
    }

    public async ValueTask<DynamicExcelBuilder<T>> OpenExcelAsync<T>(PipeReader reader, IReadOnlyList<T> source, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        BeginDiagnosticPhase("openexcel-pipe-buffer-copy");
        using MemoryStream bufferedStream = new();
        using Stream readerStream = reader.AsStream(leaveOpen: true);
        await readerStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return OpenExcel(bufferedStream, source, map);
    }

    public DynamicExcelBuilder<T> OpenExcel<T>(string filePath, List<T> source, ExcelModelMap<T> map) where T : class
    {
        using Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return OpenExcel(fileStream, source, map);
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

}
