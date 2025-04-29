using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils
{
    /// <summary>
    /// 通用 Excel 导入导出帮助类（基于 NPOI）。
    /// </summary>
    public class ExcelHelper : IExcelImport, IExcelExport, IDisposable
    {
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _typeColumnMapCache
            = [];
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _typePropsCache = [];

        private IWorkbook? _workbook = null;
        private ISheet? _sheet = null;
        private readonly Dictionary<PropertyInfo, int> _columnMap = [];
        private int _titleRowIndex = -1, _contentRowIndex = -1;
        private int _sheetIndex = 0;
        private string? _sheetName;

        #region Export

        public byte[] Export<T>(List<T> source, string sheetName)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentNullException(nameof(sheetName));

            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet(sheetName);

            // 取带有 ExcelColumnAttribute 的属性，并按 Order 排序
            var props = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.IsDefined(typeof(ExcelColumnAttribute), false))
                .OrderBy(p => p.GetCustomAttribute<ExcelColumnAttribute>()!.Order)
                .ToArray();

            // 标题行
            var titleRow = sheet.CreateRow(0);
            for (int i = 0; i < props.Length; i++)
            {
                var attr = props[i].GetCustomAttribute<ExcelColumnAttribute>()!;
                titleRow.CreateCell(i).SetCellValue(attr.Name!);
            }

            // 内容行
            for (int r = 0; r < source.Count; r++)
            {
                var row = sheet.CreateRow(r + 1);
                var item = source[r];
                for (int c = 0; c < props.Length; c++)
                {
                    var val = props[c].GetValue(item)?.ToString() ?? string.Empty;
                    row.CreateCell(c).SetCellValue(val);
                }
            }

            using var ms = new MemoryStream();
            wb.Write(ms);
            return ms.ToArray();
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

        public List<T> Import<T>(byte[] data)
            => Import<T>(Path.GetExtension("dummy" + Guid.NewGuid()) ?? ".xlsx", new MemoryStream(data));

        public List<T> Import<T>(string fileName, byte[] data)
            => Import<T>(fileName, new MemoryStream(data));

        public List<T> Import<T>(string fileName, Stream stream)
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

            // 3. 构建属性列映射
            BuildColumnMapWithCache<T>();

            // 4. 读取数据行
            return ReadDataRows<T>();
        }

        private void BuildColumnMapWithCache<T>()
        {
            var type = typeof(T);

            // 从缓存中取属性列表（已按 Order 排序）
            var props = _typePropsCache.GetOrAdd(type, t =>
                [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.IsDefined(typeof(ExcelColumnAttribute), false))
                 .OrderBy(p => p.GetCustomAttribute<ExcelColumnAttribute>()!.Order)]
            );

            // 从缓存中取“列名->Property”
            var nameMap = _typeColumnMapCache.GetOrAdd(type, t =>
            {
                var headerRow = _sheet!.GetRow(_titleRowIndex)
                                ?? throw new InvalidOperationException($"在行 {_titleRowIndex} 未发现标题行。");
                var dict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

                for (int c = 0; c < headerRow.LastCellNum; c++)
                {
                    var cell = headerRow.GetCell(c);
                    if (cell == null) continue;
                    var title = cell.ToString()?.Trim();
                    if (string.IsNullOrEmpty(title)) continue;

                    // 找到属性
                    var prop = props.FirstOrDefault(p =>
                        string.Equals(p.GetCustomAttribute<ExcelColumnAttribute>()!.Name, title, StringComparison.OrdinalIgnoreCase));
                    if (prop != null)
                        dict[title] = prop;
                }
                return dict;
            });

            // 将字典转换为 PropertyInfo->列号 的映射
            _columnMap.Clear();
            foreach (var kv in nameMap)
            {
                // 再次查列号（因为缓存 key 是列名，值是 Property）
                for (int c = 0; c < _sheet!.GetRow(_titleRowIndex)!.LastCellNum; c++)
                {
                    if (_sheet.GetRow(_titleRowIndex).GetCell(c)?.ToString()?.Trim() == kv.Key)
                    {
                        _columnMap[kv.Value] = c;
                        break;
                    }
                }
            }
        }

        private List<T> ReadDataRows<T>()
        {
            var result = new List<T>();
            var props = _columnMap.Keys.ToArray();

            for (int r = _contentRowIndex; r <= _sheet!.LastRowNum; r++)
            {
                var row = _sheet.GetRow(r);
                if (row == null) continue;

                var obj = Activator.CreateInstance<T>()!;
                bool anyCellHasValue = false;

                foreach (var prop in props)
                {
                    var colIndex = _columnMap[prop];
                    var cell = row.GetCell(colIndex);
                    if (cell == null) continue;

                    string? raw = cell.CellType switch
                    {
                        CellType.String => cell.StringCellValue,
                        CellType.Numeric when DateUtil.IsCellDateFormatted(cell) => cell.DateCellValue.ToString(),
                        CellType.Numeric => cell.NumericCellValue.ToString(),
                        CellType.Boolean => cell.BooleanCellValue.ToString(),
                        CellType.Formula => cell.ToString(),
                        _ => string.Empty
                    };

                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    anyCellHasValue = true;

                    object? converted = ConvertValue(raw, prop.PropertyType);
                    prop.SetValue(obj, converted);
                }

                if (anyCellHasValue)
                    result.Add(obj);
            }

            return result;
        }

        private static object? ConvertValue(string raw, Type targetType)
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying == typeof(string)) return raw;
            if (underlying == typeof(DateTime) && DateTime.TryParse(raw, out var dt)) return dt;
            if (underlying == typeof(bool) && bool.TryParse(raw, out var bb)) return bb;
            if (underlying == typeof(int) && int.TryParse(raw, out var ii)) return ii;
            if (underlying == typeof(long) && long.TryParse(raw, out var ll)) return ll;
            if (underlying == typeof(double) && double.TryParse(raw, out var dd)) return dd;
            if (underlying.IsEnum && Enum.TryParse(underlying, raw, true, out var eo)) return eo;
            // fallback to ChangeType
            try { return Convert.ChangeType(raw, underlying); }
            catch { return null; }
        }

        #endregion

        #region 资源释放
        public void Dispose()
        {
            // 只释放实例持有的资源
            _workbook?.Dispose();
            _sheet = null;
            _columnMap.Clear();
            // 调用 GC.SuppressFinalize 以防止派生类需要重新实现 IDisposable
            GC.SuppressFinalize(this);
        }

        public static void ClearCache()
        {
            _typePropsCache.Clear();
            _typeColumnMapCache.Clear();
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
            var type = typeof(T);

            // 1. 取已缓存的属性列表（按 Order 排序）
            var props = _typePropsCache.GetOrAdd(type, t =>
                [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.IsDefined(typeof(ExcelColumnAttribute), false))
                 .OrderBy(p => p.GetCustomAttribute<ExcelColumnAttribute>()!.Order)]
            );

            // 2. 取已缓存的“列名→PropertyInfo”
            var nameMap = _typeColumnMapCache.GetOrAdd(type, t =>
            {
                var header = sheet.GetRow(titleRowIndex)
                             ?? throw new InvalidOperationException($"在行 {titleRowIndex} 未发现标题行。");
                var dict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < header.LastCellNum; c++)
                {
                    var cell = header.GetCell(c);
                    if (cell == null) continue;
                    var title = cell.ToString()?.Trim();
                    if (string.IsNullOrEmpty(title)) continue;

                    var prop = props.FirstOrDefault(p =>
                        string.Equals(p.GetCustomAttribute<ExcelColumnAttribute>()!.Name, title, StringComparison.OrdinalIgnoreCase));
                    if (prop != null)
                        dict[title] = prop;
                }
                return dict;
            });

            // 3. 从列名→PropertyInfo 转为 PropertyInfo→列号
            var map = new Dictionary<PropertyInfo, int>();
            var headerRow = sheet.GetRow(titleRowIndex)!;
            foreach (var kv in nameMap)
            {
                // 再扫一遍列号，保证准确
                for (int c = 0; c < headerRow.LastCellNum; c++)
                {
                    if (headerRow.GetCell(c)?.ToString()?.Trim() == kv.Key)
                    {
                        map[kv.Value] = c;
                        break;
                    }
                }
            }
            return map;
        }
    }
}