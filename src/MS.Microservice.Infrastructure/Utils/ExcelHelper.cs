using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.IO.Pipelines;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils
{
    public class ExcelHelper : IExcelImport,IAsyncExcelImport, IExcelExport, IAsyncExcelExport
    {
        private static readonly ConcurrentDictionary<Type, ExcelPropertyDescriptor[]> PropertyCache = new();
        private IWorkbook? workbook;
        private ISheet? sheet;
        private int[]? columnsIndex;
        private int sheetIndex = -1, titleRowIndex = -1, contentRowIndex = -1;
        private string? _sheetName;

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

            using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
            currentWorkbook.Write(destination, leaveOpen: true);
        }

        public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, Stream destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
            await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, PipeWriter destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            using IWorkbook currentWorkbook = CreateWorkbook(source, sheetName);
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

            using IWorkbook currentWorkbook = CreateWorkbook(dt, sheetName);
            await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ExportAsync(DataTable dt, string sheetName, PipeWriter destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dt);
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            using IWorkbook currentWorkbook = CreateWorkbook(dt, sheetName);
            await WriteWorkbookAsync(currentWorkbook, destination, cancellationToken).ConfigureAwait(false);
        }

        private static IWorkbook CreateWorkbook<T>(IReadOnlyList<T> source, string sheetName)
        {
            var currentWorkbook = new XSSFWorkbook();
            var currentSheet = currentWorkbook.CreateSheet(sheetName);
            var properties = GetExcelProperties(typeof(T));
            SetExcelTitle(currentSheet, properties);
            SetExcelBody(currentSheet, source, properties);
            return currentWorkbook;
        }

        private static IWorkbook CreateWorkbook(DataTable dt, string sheetName)
        {
            var currentWorkbook = new XSSFWorkbook();
            var currentSheet = currentWorkbook.CreateSheet(sheetName);
            IRow title = currentSheet.CreateRow(0);
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                title.CreateCell(i).SetCellValue(dt.Columns[i].ColumnName.Trim());
            }

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                var dataRow = dt.Rows[i];
                IRow row = currentSheet.CreateRow(i + 1);
                foreach (DataColumn column in dt.Columns)
                {
                    object? value = dataRow[column];
                    if (value == null || value == DBNull.Value)
                    {
                        continue;
                    }

                    SetCellValue(row.CreateCell(column.Ordinal), value);
                }
            }

            return currentWorkbook;
        }

        private static void SetExcelTitle(ISheet sheet, IReadOnlyList<ExcelPropertyDescriptor> props)
        {
            IRow title = sheet.CreateRow(0);
            for (int i = 0; i < props.Count; i++)
            {
                title.CreateCell(i).SetCellValue(props[i].ColumnName);
            }
        }

        private static void SetExcelBody<T>(ISheet sheet, IReadOnlyList<T> source, IReadOnlyList<ExcelPropertyDescriptor> props)
        {
            if (source.Count == 0)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                IRow row = sheet.CreateRow(i + 1);
                var obj = source[i];
                for (int j = 0; j < props.Count; j++)
                {
                    var val = props[j].Property.GetValue(obj);
                    if (val == null)
                    {
                        continue;
                    }

                    SetCellValue(row.CreateCell(j), val);
                }
            }
        }

        public List<T> Import<T>(byte[] data) where T : class, new()
        {
            using MemoryStream ms = new(data, writable: false);
            return Import<T>("unknown.xlsx", ms);
        }

        public async ValueTask<List<T>> ImportAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await ImportAsync<T>("unknown.xlsx", data, cancellationToken);
        }

        private void ReadRow(IReadOnlyList<ExcelPropertyDescriptor> properties)
        {
            var titleRow = sheet?.GetRow(titleRowIndex) ?? throw new InvalidOperationException("未找到标题行");
            columnsIndex = new int[titleRow.LastCellNum];
            Array.Fill(columnsIndex, -1);

            for (int i = 0; i < titleRow.LastCellNum; i++)
            {
                ICell? cell = titleRow.GetCell(i);
                if (cell == null)
                {
                    continue;
                }

                var excelTitle = cell.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(excelTitle))
                {
                    continue;
                }

                for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    if (string.Equals(properties[propertyIndex].ColumnName, excelTitle, StringComparison.Ordinal))
                    {
                        columnsIndex[i] = propertyIndex;
                        break;
                    }
                }
            }
        }

        private List<T> ReadBody<T>(IReadOnlyList<ExcelPropertyDescriptor> properties, DataFormatter formatter, IFormulaEvaluator evaluator)
        {
            ArgumentNullException.ThrowIfNull(columnsIndex);

            var targetSheet = sheet ?? throw new InvalidOperationException("未找到工作表");
            var list = new List<T>(Math.Max(targetSheet.LastRowNum - contentRowIndex + 1, 0));
            for (int i = contentRowIndex; i <= targetSheet.LastRowNum; i++)
            {
                IRow? row = targetSheet.GetRow(i);
                if (row == null)
                {
                    continue;
                }

                T obj = Activator.CreateInstance<T>();
                for (int j = 0; j < columnsIndex.Length; j++)
                {
                    var propertyLocation = columnsIndex[j];
                    if (propertyLocation == -1)
                    {
                        continue;
                    }

                    ICell? cell = row.GetCell(j);
                    if (cell == null)
                    {
                        continue;
                    }

                    if (properties[propertyLocation].TargetType == typeof(DateTime)
                        && TryGetDateCellValue(cell, out DateTime dateValue))
                    {
                        SetPropertyValue(obj, properties[propertyLocation], dateValue);
                        continue;
                    }

                    var value = formatter.FormatCellValue(cell, evaluator);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    SetPropertyValue(obj, properties[propertyLocation], value);
                }

                list.Add(obj);
            }

            return list;
        }

        public List<T> Import<T>(string fileName, byte[] data) where T : class, new()
        {
            using MemoryStream ms = new(data, writable: false);
            return Import<T>(fileName, ms);
        }

        public async ValueTask<List<T>> ImportAsync<T>(string fileName, byte[] data, CancellationToken cancellationToken = default) where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream ms = new(data, writable: false);
            return await ImportAsync<T>(fileName, ms, cancellationToken).ConfigureAwait(false);
        }

        public List<T> Import<T>(string fileName, Stream stream) where T : class, new()
        {
            try
            {
                ArgumentNullException.ThrowIfNull(stream);
                PrepareStreamForRead(stream);

                workbook = CreateWorkbookForRead(fileName, stream);
                if (!string.IsNullOrEmpty(_sheetName))
                {
                    sheet = workbook.GetSheet(_sheetName) ?? throw new InvalidOperationException($"工作表 '{_sheetName}' 不存在");
                }
                else
                {
                    if (sheetIndex == -1)
                    {
                        AutoAnalyzeSheetIndex();
                    }
                    sheet = workbook.GetSheetAt(sheetIndex);
                }

                if (titleRowIndex == -1 || contentRowIndex == -1)
                {
                    throw new InvalidOperationException($"无效操作：请初始化 {nameof(titleRowIndex)} 与 {nameof(contentRowIndex)}，您在解析文件之前应调用方法 InitStartReadRowIndex");
                }

                var properties = GetExcelProperties(typeof(T));
                var formatter = new DataFormatter(CultureInfo.InvariantCulture);
                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                ReadRow(properties);
                return ReadBody<T>(properties, formatter, evaluator);
            }
            catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw new InvalidOperationException("模板解析错误，请确认导入的模板格式", ex);
            }
        }

        public async ValueTask<List<T>> ImportAsync<T>(string fileName, Stream stream, CancellationToken cancellationToken = default) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(stream);
            cancellationToken.ThrowIfCancellationRequested();

            if (stream.CanSeek)
            {
                return Import<T>(fileName, stream);
            }

            using MemoryStream bufferedStream = new();
            await stream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
            bufferedStream.Position = 0;
            return Import<T>(fileName, bufferedStream);
        }

        public async ValueTask<List<T>> ImportAsync<T>(string fileName, PipeReader reader, CancellationToken cancellationToken = default) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(reader);
            cancellationToken.ThrowIfCancellationRequested();

            using MemoryStream bufferedStream = new();
            using Stream readerStream = reader.AsStream(leaveOpen: true);
            await readerStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
            bufferedStream.Position = 0;
            return Import<T>(fileName, bufferedStream);
        }

        public ExcelHelper InitSheetIndex(int sheetIndex)
        {
            this.sheetIndex = sheetIndex;
            _sheetName = null;
            return this;
        }

        public ExcelHelper InitSheetName(string sheetName)
        {
            _sheetName = sheetName ?? throw new ArgumentNullException(nameof(sheetName));
            sheetIndex = -1;
            return this;
        }

        public ExcelHelper InitStartReadRowIndex(int titleRowIndex, int contentRowIndex)
        {
            this.titleRowIndex = titleRowIndex;
            this.contentRowIndex = contentRowIndex;
            return this;
        }

        private void AutoAnalyzeSheetIndex()
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook), "文件读取失败");
            }

            var sheetCount = workbook.NumberOfSheets;
            if (sheetCount <= 0)
            {
                throw new InvalidOperationException("模板中不存在工作表");
            }

            sheetIndex = sheetCount - 1;
        }

        public static Dictionary<PropertyInfo, int> BuildColumnMap<T>(ISheet sheet, int headerRowIndex)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            var properties = GetExcelProperties(typeof(T));
            var headerRow = sheet.GetRow(headerRowIndex) ?? throw new InvalidOperationException("Header row not found");
            var result = new Dictionary<PropertyInfo, int>();
            for (int i = 0; i < headerRow.LastCellNum; i++)
            {
                var cell = headerRow.GetCell(i);
                if (cell == null) continue;
                var headerText = cell.ToString()?.Trim();
                if (string.IsNullOrEmpty(headerText)) continue;
                foreach (var prop in properties)
                {
                    if (string.Equals(prop.ColumnName, headerText, StringComparison.Ordinal))
                    {
                        result[prop.Property] = i;
                        break;
                    }
                }
            }
            return result;
        }

        public DynamicExcelBuilder<T> OpenExcel<T>(IReadOnlyList<T> items, Stream template, string sheetName, int titleRowIndex)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentException.ThrowIfNullOrEmpty(sheetName);
            PrepareStreamForRead(template);
            IWorkbook wb = WorkbookFactory.Create(template);
            ISheet sheetAt = wb.GetSheet(sheetName) ?? throw new InvalidOperationException($"工作表 '{sheetName}' 不存在");
            return new DynamicExcelBuilder<T>(wb, sheetAt, items, titleRowIndex);
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
            int resolvedSheetIndex = sheetIndex >= 0 ? sheetIndex : currentWorkbook.NumberOfSheets - 1;
            if (resolvedSheetIndex < 0)
            {
                throw new InvalidOperationException("模板中不存在工作表");
            }

            sheetIndex = resolvedSheetIndex;
            ISheet sheetAt = currentWorkbook.GetSheetAt(resolvedSheetIndex);
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

        private static ExcelPropertyDescriptor[] GetExcelProperties(Type type)
        {
            return PropertyCache.GetOrAdd(type, static currentType =>
                currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => new
                    {
                        Property = property,
                        Attribute = property.GetCustomAttribute<ExcelColumnAttribute>(inherit: false)
                    })
                    .Where(item => item.Attribute?.Ignore != true)
                    .OrderBy(item => item.Attribute?.Order ?? int.MaxValue)
                    .Select(item => new ExcelPropertyDescriptor(
                        item.Property,
                        item.Attribute,
                        Nullable.GetUnderlyingType(item.Property.PropertyType) ?? item.Property.PropertyType,
                        string.IsNullOrWhiteSpace(item.Attribute?.Name) ? item.Property.Name : item.Attribute!.Name!.Trim()))
                    .ToArray());
        }

        private static void SetPropertyValue<T>(T target, ExcelPropertyDescriptor property, string value)
        {
            if (!TryConvertValue(value, property.TargetType, out object? convertedValue))
            {
                return;
            }

            property.Property.SetValue(target, convertedValue);
        }

        private static void SetPropertyValue<T>(T target, ExcelPropertyDescriptor property, DateTime value)
        {
            property.Property.SetValue(target, value);
        }

        private static bool TryConvertValue(string value, Type targetType, out object? convertedValue)
        {
            if (targetType == typeof(string))
            {
                convertedValue = value;
                return true;
            }

            if (targetType == typeof(DateTime))
            {
                var parsed = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime)
                    || DateTime.TryParse(value, out dateTime);
                convertedValue = parsed ? dateTime : null;
                return parsed;
            }

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

            switch (Type.GetTypeCode(targetType))
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

        private static void SetCellValue(ICell cell, object value)
        {
            switch (value)
            {
                case string stringValue:
                    cell.SetCellValue(stringValue);
                    break;
                case DateTime dateTimeValue:
                    cell.SetCellValue(dateTimeValue);
                    ApplyDateStyle(cell);
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

        private static bool TryGetDateCellValue(ICell cell, out DateTime value)
        {
            if (cell.CellType == CellType.Numeric || cell.CellType == CellType.Formula)
            {
                try
                {
                    var dateCellValue = cell.DateCellValue;
                    if (dateCellValue.HasValue)
                    {
                        value = dateCellValue.Value;
                        return true;
                    }
                }
                catch
                {
                    if (DateUtil.IsValidExcelDate(cell.NumericCellValue))
                    {
                        value = DateUtil.GetJavaDate(cell.NumericCellValue);
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static void ApplyDateStyle(ICell cell)
        {
            IWorkbook workbook = cell.Sheet.Workbook;
            ICellStyle style = workbook.CreateCellStyle();
            style.CloneStyleFrom(cell.CellStyle);
            style.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd HH:mm:ss");
            cell.CellStyle = style;
        }

        private sealed record ExcelPropertyDescriptor(PropertyInfo Property, ExcelColumnAttribute? Attribute, Type TargetType, string ColumnName);
    }
}
