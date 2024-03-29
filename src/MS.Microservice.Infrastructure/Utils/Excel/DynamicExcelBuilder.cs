﻿using NPOI.SS.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public class DynamicExcelBuilder<T>(IWorkbook workbook, ISheet sheetAt, List<T> source)
    {
        private readonly IWorkbook _workbook = workbook;
        private readonly ISheet _sheet = sheetAt;
        private readonly List<T> _items = source;
        private readonly Dictionary<string, int> _columnMapping = [];

        public DynamicExcelBuilder<T> InitInsertRow(int titleRowIndex, int startRowIndex)
        {
            int itemCount = _items.Count;
            _sheet.ShiftRows(startRowIndex, _sheet.LastRowNum + itemCount + 1, itemCount);
            IRow titleRow = _sheet.GetRow(titleRowIndex);
            ICellStyle titleCellStyle = titleRow.Cells.FirstOrDefault()?.CellStyle!;

            for (int i = startRowIndex; i < startRowIndex + _items.Count; i++)
            {
                IRow contentRow = _sheet.CreateRow(i);
                contentRow.Height = titleRow.Height;

                foreach (ICell cell in titleRow.Cells)
                {
                    ICell contentCell = contentRow.CreateCell(cell.ColumnIndex);
                    contentCell.CellStyle = titleCellStyle;
                }
            }
            ReadTitle(titleRowIndex);
            return this;
        }

        private void ReadTitle(int titleRowIndex)
        {
            var titles = _sheet.GetRow(titleRowIndex).Cells;
            // 建立属性名与列索引的映射关系
            PropertyInfo[] properties = typeof(T).GetTypeInfo().GetProperties();
            if (titles.Count == 0)
            {
                for (int columnIndex = 0; columnIndex < properties.Length; columnIndex++)
                {
                    ExcelColumnAttribute? attribute = properties[columnIndex].GetCustomAttribute<ExcelColumnAttribute>();
                    if (attribute != null)
                    {
                        _columnMapping[properties[columnIndex].Name] = columnIndex;
                    }
                }
            }
            else
            {
                for (int i = 0; i < titles.Count; i++)
                {
                    string title = titles[i].StringCellValue;
                    _columnMapping[title] = i;
                }
            }
        }

        public DynamicExcelBuilder<T> InsertCellValue(int contentRowIndex)
        {
            PropertyInfo[] properties = typeof(T).GetTypeInfo().GetProperties();
            for (int i = 0; i < Items.Count; i++)
            {
                IRow row = _sheet.GetRow(contentRowIndex++);
                T obj = Items[i];
                foreach (PropertyInfo prop in properties)
                {
                    string propertyName = prop.GetCustomAttribute<ExcelColumnAttribute>()?.Name ?? "";

                    if (_columnMapping.TryGetValue(propertyName, out int columnIndex))
                    {
                        ICell cell = row.CreateCell(columnIndex);
                        object? value = prop.GetValue(obj);
                        if (value != null)
                        {
                            cell.SetCellValue(value.ToString());
                        }
                    }
                }
            }
            return this;
        }

        public async Task<byte[]> WriteAsync()
        {
            using MemoryStream stream = new();
            _workbook.Write(stream, leaveOpen:true);
            byte[] buffer = new byte[stream.Length];
            stream.Seek(0L, SeekOrigin.Begin);
            await stream.ReadAsync(buffer);
            return buffer;
        }

        public IWorkbook Workbook { get { return _workbook; } }

        public List<T> Items { get { return _items; } }
    }
}
