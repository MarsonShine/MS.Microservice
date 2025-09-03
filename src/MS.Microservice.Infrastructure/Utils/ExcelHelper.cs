using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils
{
    /// <summary>
    /// 通用 Excel 导入导出帮助类（基于 NPOI）。
    /// </summary>
    public class ExcelHelper : IExcelImport, IExcelExport, IDisposable
    {
        // 类型级别的强缓存：包含已排序列定义、已编译 Getter/Setter、构造委托等（与具体 sheet 无关）
        private static readonly ConcurrentDictionary<Type, TypeMeta> _typeMetaCache = [];
        // 当前实例针对选中 sheet 的列映射（与 sheet 的标题行有关）
        private readonly Dictionary<PropertyInfo, int> _columnMap = [];

        private IWorkbook? _workbook = null;
        private ISheet? _sheet = null;
        private ColumnMeta[]? _activeColumns;// 本次实际匹配到的列（按声明顺序，便于顺序遍历）

        private int _titleRowIndex = -1, _contentRowIndex = -1;
        private int _sheetIndex = 0;
        private string? _sheetName;

        #region Export
        public byte[] Export<T>(List<T> source, string sheetName)
        {
            using var ms = new MemoryStream();
            ExportToStream<T>(source, sheetName, ms);
            return ms.ToArray();
        }

        public void ExportToStream<T>(List<T> source, string sheetName, Stream destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentNullException(nameof(sheetName));

            var meta = GetTypeMeta(typeof(T));
            using var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet(sheetName);

            // 预创建日期样式
            var dateStyle = wb.CreateCellStyle();
            dateStyle.DataFormat = wb.CreateDataFormat().GetFormat("yyyy-mm-dd");

            // 标题
            var titleRow = sheet.CreateRow(0);
            for (int i = 0; i < meta.Columns.Length; i++)
                titleRow.CreateCell(i).SetCellValue(meta.Columns[i].Title);

            // 行
            for (int r = 0; r < source.Count; r++)
            {
                var row = sheet.CreateRow(r + 1);
                var item = source[r];

                for (int c = 0; c < meta.Columns.Length; c++)
                {
                    var col = meta.Columns[c];
                    var val = col.Getter(item!);
                    var cell = row.CreateCell(c);

                    if (val is null) { cell.SetCellValue(string.Empty); continue; }

                    switch (val)
                    {
                        case string s: cell.SetCellValue(s); break;
                        case DateTime dt: cell.SetCellValue(dt); cell.CellStyle = dateStyle; break;
                        case bool b: cell.SetCellValue(b); break;
                        case double d: cell.SetCellValue(d); break;
                        case float f: cell.SetCellValue(f); break;
                        case decimal m: cell.SetCellValue((double)m); break;
                        case int i32: cell.SetCellValue(i32); break;
                        case long i64: cell.SetCellValue((double)i64); break;
                        case short i16: cell.SetCellValue(i16); break;
                        case byte u8: cell.SetCellValue(u8); break;
                        case sbyte s8: cell.SetCellValue((double)s8); break;
                        case uint u32: cell.SetCellValue(u32); break;
                        case ulong u64: cell.SetCellValue((double)u64); break;
                        case ushort u16: cell.SetCellValue(u16); break;
                        case Enum e: cell.SetCellValue(e.ToString()); break;
                        default: cell.SetCellValue(val.ToString()); break;
                    }
                }
            }

            wb.Write(destination, leaveOpen: false); // 无 ToArray 复制
        }
        #endregion

        #region Import
        public ExcelHelper InitSheetIndex(int sheetIndex)
        {
            _sheetIndex = sheetIndex;
            _sheetName = null;
            return this;
        }

        public ExcelHelper InitSheetName(string sheetName)
        {
            _sheetName = sheetName ?? throw new ArgumentNullException(nameof(sheetName));
            return this;
        }

        /// <summary>titleRowIndex：列名所在行索引；contentRowIndex：数据起始行索引</summary>
        public ExcelHelper InitStartReadRowIndex(int titleRowIndex, int contentRowIndex)
        {
            _titleRowIndex = titleRowIndex;
            _contentRowIndex = contentRowIndex;
            return this;
        }

        public List<T> Import<T>(byte[] data) where T : class, new()
            => Import<T>(".xlsx", new MemoryStream(data));

        public List<T> Import<T>(string fileName, byte[] data) where T : class, new()
            => Import<T>(fileName, new MemoryStream(data));

        public List<T> Import<T>(string fileName, Stream stream) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (_titleRowIndex < 0 || _contentRowIndex < 0)
                throw new InvalidOperationException("请先调用 InitStartReadRowIndex 初始化 titleRowIndex 与 contentRowIndex。");

            // 1. 组装 Workbook
            stream.Seek(0, SeekOrigin.Begin);
            _workbook = fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                ? new HSSFWorkbook(stream)
                : new XSSFWorkbook(stream);

            // 2. 选择 Sheet
            _sheet = !string.IsNullOrEmpty(_sheetName)
                ? _workbook.GetSheet(_sheetName!) ?? throw new ArgumentException($"不存在名为 \"{_sheetName}\" 的工作表。")
                : _workbook.GetSheetAt(_sheetIndex);

            // 3. 构建属性列映射（类型元数据 + 当前 sheet 标题行 -> 列索引）
            BuildColumnMap<T>();

            // 4. 读取数据行
            return ReadDataRows<T>();
        }

        public IEnumerable<T> ImportAsEnumerable<T>(string fileName, Stream stream) where T : class, new()
        {
            var list = Import<T>(fileName, stream);
            foreach (var item in list) yield return item; // 若要彻底流式，需重构内部从 “返回 List” 改为“逐行 yield”
        }

        private void BuildColumnMap<T>()
        {
            var meta = GetTypeMeta(typeof(T));
            var headerRow = _sheet!.GetRow(_titleRowIndex)
                            ?? throw new InvalidOperationException($"在行 {_titleRowIndex} 未发现标题行。");

            // 将标题行一次性读入 Dictionary，避免多重循环 O(H^2)
            var headerIndex = new Dictionary<string, int>(headerRow.LastCellNum, StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headerRow.LastCellNum; c++)
            {
                var title = headerRow.GetCell(c)?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(title) && !headerIndex.ContainsKey(title))
                    headerIndex[title] = c;
            }

            _columnMap.Clear();
            var active = new List<ColumnMeta>(meta.Columns.Length);

            foreach (var col in meta.Columns)
            {
                if (headerIndex.TryGetValue(col.Title, out var idx))
                {
                    _columnMap[col.Property] = idx;
                    active.Add(col);
                }
            }

            _activeColumns = [.. active];
        }

        private List<T> ReadDataRows<T>() where T : class, new()
        {
            var result = new List<T>();
            var meta = GetTypeMeta(typeof(T));
            var cols = _activeColumns ?? meta.Columns; // 理论上 _activeColumns 已初始化

            var lastRow = _sheet!.LastRowNum;
            var expected = Math.Max(0, lastRow - _contentRowIndex + 1);
            if (expected > 0) result.EnsureCapacity(expected);

            for (int r = _contentRowIndex; r <= lastRow; r++)
            {
                var row = _sheet.GetRow(r);
                if (row == null) continue;

                var obj = (T)meta.Ctor();
                bool anyCellHasValue = false;

                for (int i = 0; i < cols.Length; i++)
                {
                    var col = cols[i];
                    if (!_columnMap.TryGetValue(col.Property, out var colIndex)) continue;

                    var cell = row.GetCell(colIndex);
                    if (cell == null) continue;

                    var raw = GetRawCellValue(cell);
                    if (raw is null) continue;
                    if (raw is string s && string.IsNullOrWhiteSpace(s)) continue;

                    var converted = ConvertValueFast(raw, col.UnderlyingType); // 使用 fast-path
                    if (converted is null) continue;

                    col.Setter(obj!, converted);
                    anyCellHasValue = true;
                }

                if (anyCellHasValue)
                    result.Add(obj);
            }

            return result;
        }

        private static object? GetRawCellValue(ICell cell)
        {
            // 优先使用原生类型，减少字符串中转与解析
            return cell.CellType switch
            {
                CellType.String => cell.StringCellValue,
                CellType.Boolean => cell.BooleanCellValue,
                CellType.Numeric when DateUtil.IsCellDateFormatted(cell) => cell.DateCellValue,
                CellType.Numeric => cell.NumericCellValue, // double
                CellType.Formula => cell.CachedFormulaResultType switch
                {
                    CellType.String => cell.StringCellValue,
                    CellType.Boolean => cell.BooleanCellValue,
                    CellType.Numeric when DateUtil.IsCellDateFormatted(cell) => cell.DateCellValue,
                    CellType.Numeric => cell.NumericCellValue,
                    _ => cell.ToString()
                },
                _ => null
            };
        }

        private static object? ConvertValue(object raw, Type targetType)
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // 已是目标类型
            if (underlying.IsInstanceOfType(raw)) return raw;

            try
            {
                switch (raw)
                {
                    case string s:
                        return ConvertFromString(s, underlying);
                    case DateTime dt:
                        if (underlying == typeof(DateTime)) return dt;
                        if (underlying == typeof(string)) return dt.ToString();
                        return null;

                    case bool b:
                        if (underlying == typeof(bool)) return b;
                        if (underlying == typeof(string)) return b.ToString();
                        return null;

                    case double d:
                        if (underlying == typeof(double)) return d;
                        if (underlying == typeof(float)) return (float)d;
                        if (underlying == typeof(decimal)) return (decimal)d;
                        if (underlying == typeof(int)) return (int)d;
                        if (underlying == typeof(long)) return (long)d;
                        if (underlying == typeof(string)) return d.ToString();
                        if (underlying == typeof(DateTime))
                            return DateUtil.GetJavaDate(d); // 数字转日期（Excel 存储）
                        return null;

                    default:
                        // 其他数值类型或枚举
                        if (underlying.IsEnum)
                        {
                            if (raw is string es && Enum.TryParse(underlying, es, true, out var ev)) return ev;
                            var num = System.Convert.ChangeType(raw, Enum.GetUnderlyingType(underlying));
                            return Enum.ToObject(underlying, num!);
                        }
                        return System.Convert.ChangeType(raw, underlying);
                }
            }
            catch
            {
                return null;
            }
        }

        // Fast-path：目标类型已知为非可空类型
        private static object? ConvertValueFast(object raw, Type underlying)
        {
            try
            {
                switch (raw)
                {
                    case string s:
                        return ConvertFromString(s, underlying);
                    case DateTime dt:
                        if (underlying == typeof(DateTime)) return dt;
                        if (underlying == typeof(string)) return dt.ToString();
                        return null;
                    case bool b:
                        if (underlying == typeof(bool)) return b;
                        if (underlying == typeof(string)) return b.ToString();
                        return null;
                    case double d:
                        if (underlying == typeof(double)) return d;
                        if (underlying == typeof(float)) return (float)d;
                        if (underlying == typeof(decimal)) return (decimal)d;
                        if (underlying == typeof(int)) return (int)d;
                        if (underlying == typeof(long)) return (long)d;
                        if (underlying == typeof(string)) return d.ToString();
                        if (underlying == typeof(DateTime)) return DateUtil.GetJavaDate(d);
                        return null;
                    default:
                        if (underlying.IsEnum)
                        {
                            if (raw is string es && Enum.TryParse(underlying, es, true, out var ev)) return ev;
                            var num = Convert.ChangeType(raw, Enum.GetUnderlyingType(underlying));
                            return Enum.ToObject(underlying, num!);
                        }
                        return Convert.ChangeType(raw, underlying);
                }
            }
            catch
            {
                return null;
            }
        }

        private static object ConvertFromString(string s, Type underlying)
        {
            if (underlying == typeof(string)) return s;
            if (underlying == typeof(DateTime) && DateTime.TryParse(s, out var dt)) return dt;
            if (underlying == typeof(bool) && bool.TryParse(s, out var bb)) return bb;
            if (underlying == typeof(int) && int.TryParse(s, out var ii)) return ii;
            if (underlying == typeof(long) && long.TryParse(s, out var ll)) return ll;
            if (underlying == typeof(double) && double.TryParse(s, out var dd)) return dd;
            if (underlying == typeof(float) && float.TryParse(s, out var ff)) return ff;
            if (underlying == typeof(decimal) && decimal.TryParse(s, out var mm)) return mm;
            if (underlying.IsEnum && Enum.TryParse(underlying, s, true, out var eo)) return eo;
            // 兜底
            return Convert.ChangeType(s, underlying);
        }

        #endregion

        #region 资源释放
        public void Dispose()
        {
            // 只释放实例持有的资源
            _workbook?.Dispose();
            _sheet = null;
            _activeColumns = null;
            _columnMap.Clear();
            // 调用 GC.SuppressFinalize 以防止派生类需要重新实现 IDisposable
            GC.SuppressFinalize(this);
        }

        public static void ClearCache()
        {
            _typeMetaCache.Clear();
        }
        #endregion

        public DynamicExcelBuilder<T> OpenExcel<T>(List<T> source, Stream fileStream, string sheetName, int titleRowIndex = 0)
        {
            using Stream stream = fileStream;
            IWorkbook workbook = WorkbookFactory.Create(stream);
            ISheet sheetAt = workbook.GetSheet(sheetName);
            return new DynamicExcelBuilder<T>(workbook, sheetAt, source, titleRowIndex);
        }

        public DynamicExcelBuilder<T> OpenExcel<T>(List<T> source, string filePath, string sheetName)
        {
            using Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return OpenExcel(source, fileStream, sheetName);
        }

        public static Dictionary<PropertyInfo, int> BuildColumnMap<T>(ISheet sheet, int titleRowIndex)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            var headerRow = sheet.GetRow(titleRowIndex)
                           ?? throw new InvalidOperationException($"在行 {titleRowIndex} 未发现标题行。");
            var meta = GetTypeMeta(typeof(T));

            var headerIndex = new Dictionary<string, int>(headerRow.LastCellNum, StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headerRow.LastCellNum; c++)
            {
                var cell = headerRow.GetCell(c);
                if (cell == null) continue;

                string? title = null;
                if (cell.CellType == CellType.String) title = cell.StringCellValue; //先走 StringCellValue，减少 ToString 分配
                else title = cell.ToString();

                title = title?.Trim();
                if (!string.IsNullOrEmpty(title) && !headerIndex.ContainsKey(title))
                    headerIndex[title] = c;
            }

            var map = new Dictionary<PropertyInfo, int>();
            foreach (var col in meta.Columns)
            {
                if (headerIndex.TryGetValue(col.Title, out var idx))
                    map[col.Property] = idx;
            }
            return map;
        }

        #region Type metadata
        private sealed class TypeMeta
        {
            public required ColumnMeta[] Columns { get; init; }
            public required Func<object> Ctor { get; init; }
        }

        private sealed class ColumnMeta
        {
            public required PropertyInfo Property { get; init; }
            public required string Title { get; init; }
            public required int Order { get; init; }
            public required Func<object, object?> Getter { get; init; }
            public required Action<object, object?> Setter { get; init; }
            public required Type UnderlyingType { get; init; }
        }

        private static TypeMeta GetTypeMeta(Type type)
        {
            return _typeMetaCache.GetOrAdd(type, t =>
            {
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Select(p => new
                     {
                         Prop = p,
                         Attr = p.GetCustomAttribute<ExcelColumnAttribute>(false)
                     })
                     .Where(x => x.Attr?.Ignore != true) // 支持“无特性默认使用属性名”的行为
                     .OrderBy(x => x.Attr?.Order ?? int.MaxValue)
                     .ThenBy(x => x.Prop.MetadataToken)
                     .ToArray();

                var columns = new List<ColumnMeta>(props.Length);
                foreach (var x in props)
                {
                    var title = string.IsNullOrWhiteSpace(x.Attr?.Name) ? x.Prop.Name : x.Attr!.Name!.Trim();
                    var getter = CompileGetter(t, x.Prop);
                    var setter = CompileSetter(t, x.Prop);
                    var underlying = Nullable.GetUnderlyingType(x.Prop.PropertyType) ?? x.Prop.PropertyType;

                    columns.Add(new ColumnMeta
                    {
                        Property = x.Prop,
                        Title = title,
                        Order = x.Attr?.Order ?? int.MaxValue,
                        Getter = getter,
                        Setter = setter,
                        UnderlyingType = underlying
                    });
                }

                var ctor = CompileCtor(t);
                return new TypeMeta { Columns = [.. columns], Ctor = ctor };
            });
        }

        private static Func<object> CompileCtor(Type t)
        {
            var ci = t.GetConstructor(Type.EmptyTypes)
                     ?? throw new InvalidOperationException($"{t.FullName} 缺少无参构造函数。");
            var newExpr = Expression.New(ci);
            var lambda = Expression.Lambda<Func<object>>(Expression.Convert(newExpr, typeof(object)));
            return lambda.Compile();
        }

        private static Func<object, object?> CompileGetter(Type declaringType, PropertyInfo prop)
        {
            var getMi = prop.GetGetMethod(true)!;
            var objParam = Expression.Parameter(typeof(object), "obj");
            var castObj = Expression.Convert(objParam, declaringType);
            var call = Expression.Call(castObj, getMi);
            var result = Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<object, object?>>(result, objParam).Compile();
        }

        private static Action<object, object?> CompileSetter(Type declaringType, PropertyInfo prop)
        {
            var setMi = prop.GetSetMethod(true);
            if (setMi == null)
            {
                // 不可写属性：返回空实现以避免判空分支
                return static (_, __) => { };
            }

            var objParam = Expression.Parameter(typeof(object), "obj");
            var valParam = Expression.Parameter(typeof(object), "val");
            var castObj = Expression.Convert(objParam, declaringType);
            var castVal = Expression.Convert(valParam, prop.PropertyType);
            var call = Expression.Call(castObj, setMi, castVal);
            return Expression.Lambda<Action<object, object?>>(call, objParam, valParam).Compile();
        }
        #endregion
    }
}