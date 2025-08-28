using NPOI.SS.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public class DynamicExcelBuilder<T>(IWorkbook workbook, ISheet sheetAt, List<T> source, int titleRowIndex)
    {
        private readonly IWorkbook _workbook = workbook;
        private readonly ISheet _sheet = sheetAt;
        private readonly List<T> _items = source;
        private readonly Dictionary<PropertyInfo, int> _columnMap = ExcelHelper.BuildColumnMap<T>(sheetAt, titleRowIndex);
        private readonly int _titleRowIndex = titleRowIndex;

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
