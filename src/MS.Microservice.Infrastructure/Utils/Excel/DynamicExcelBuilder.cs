using NPOI.SS.UserModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public class DynamicExcelBuilder<T>
    {
        private enum CellWriteMode { Typed, Text }
        private readonly IWorkbook _workbook;
        private readonly ISheet _sheet;
        private readonly List<T> _items;
        private readonly Dictionary<PropertyInfo, int> _columnMap;
        private readonly int _titleRowIndex;

        // 为映射到列的属性编译 Getter，避免反射 GetValue
        private readonly Dictionary<PropertyInfo, Func<T, object?>> _getters = [];
        private CellWriteMode _writeMode = CellWriteMode.Typed; // 默认高性能：强类型写入
        public DynamicExcelBuilder(IWorkbook workbook, ISheet sheetAt, List<T> source, int titleRowIndex)
        {
            _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
            _sheet = sheetAt ?? throw new ArgumentNullException(nameof(sheetAt));
            _items = source ?? [];
            _titleRowIndex = titleRowIndex;

            _columnMap = ExcelHelper.BuildColumnMap<T>(_sheet, _titleRowIndex);
            _getters = BuildGetters(_columnMap.Keys);
        }

        private static Dictionary<PropertyInfo, Func<T, object?>> BuildGetters(IEnumerable<PropertyInfo> props)
        {
            var dict = new Dictionary<PropertyInfo, Func<T, object?>>();
            foreach (var prop in props)
            {
                var param = Expression.Parameter(typeof(T), "x");
                // 兼容声明类型与泛型 T 不一致的场景
                var instance = prop.DeclaringType != typeof(T)
                    ? Expression.Convert(param, prop.DeclaringType!)
                    : param as Expression;
                var access = Expression.Property(instance!, prop);
                var box = Expression.Convert(access, typeof(object));
                var lambda = Expression.Lambda<Func<T, object?>>(box, param).Compile();
                dict[prop] = lambda;
            }
            return dict;
        }

        public DynamicExcelBuilder<T> UseTextCells()
        {
            _writeMode = CellWriteMode.Text;
            return this;
        }

        public DynamicExcelBuilder<T> UseTypedCells()
        {
            _writeMode = CellWriteMode.Typed;
            return this;
        }

        public DynamicExcelBuilder<T> InitInsertRow(int startRowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(startRowIndex, 0);

            int count = _items?.Count ?? 0;
            if (count <= 0) return this;

            int lastRow = _sheet.LastRowNum;
            // 仅当起始行在现有最后一行之内时才移动（避免 NPOI 要求 first<=last 的异常）
            if (startRowIndex <= lastRow)
            {
                _sheet.ShiftRows(startRowIndex, lastRow, count);
            }

            var titleRow = _sheet.GetRow(_titleRowIndex)
                           ?? throw new ArgumentException($"行 {_titleRowIndex} 不存在");
            var style = titleRow.Cells.FirstOrDefault()?.CellStyle;

            for (int i = 0; i < count; i++)
            {
                var row = _sheet.CreateRow(startRowIndex + i);
                row.Height = titleRow.Height;
                if (style != null)
                {
                    foreach (var cell in titleRow.Cells)
                    {
                        row.CreateCell(cell.ColumnIndex).CellStyle = style;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// 按已有 _columnMap，将 Items 的属性值写入对应列
        /// </summary>
        public DynamicExcelBuilder<T> InsertCellValue(int startRowIndex)
        {
            if (_writeMode == CellWriteMode.Text)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var row = _sheet.GetRow(startRowIndex + i)
                           ?? _sheet.CreateRow(startRowIndex + i);
                    var obj = _items[i];
                    foreach (var kv in _columnMap)
                    {
                        var prop = kv.Key;
                        var col = kv.Value;
                        var cell = row.GetCell(col) ?? row.CreateCell(col);
                        var val = prop.GetValue(obj);
                        cell.SetCellValue(val?.ToString() ?? string.Empty);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var row = _sheet.GetRow(startRowIndex + i) ?? _sheet.CreateRow(startRowIndex + i);
                    var obj = _items[i];

                    foreach (var kv in _columnMap)
                    {
                        var prop = kv.Key;
                        var col = kv.Value;
                        var cell = row.GetCell(col) ?? row.CreateCell(col);

                        var val = _getters.TryGetValue(prop, out var getter) ? getter(obj) : prop.GetValue(obj);

                        if (val is null) { cell.SetCellValue(string.Empty); continue; }
                        switch (val)
                        {
                            case string s: cell.SetCellValue(s); break;
                            case DateTime dt: cell.SetCellValue(dt); break;
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
            }
            return this;
        }

        public async Task<byte[]> WriteAsync()
        {
            using var ms = new MemoryStream();
            _workbook.Write(ms, leaveOpen: true);
            return await Task.FromResult(ms.ToArray());
        }

        public IWorkbook Workbook { get { return _workbook; } }

        public List<T> Items { get { return _items; } }
    }
}
