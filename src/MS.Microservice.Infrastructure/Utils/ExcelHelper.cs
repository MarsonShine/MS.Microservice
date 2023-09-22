using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.Streaming;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils
{
    public class ExcelHelper : IExcelImport, IExcelExport
    {
        private IWorkbook? workbook;
        private ISheet? sheet;
        private int[]? columnsIndex;
        private int sheetIndex = -1, titleRowIndex = -1, contentRowIndex = -1;

        public IWorkbook? Workbook => workbook;
        public byte[] Export<T>(List<T> source, string sheetName)
        {
            var myWorkBook = new SXSSFWorkbook();
            var mySheet = myWorkBook.CreateSheet(sheetName);
            var properties = typeof(T).GetProperties()
                    .Where(p => p.CustomAttributes.Any())
                    .OrderBy(p => p.GetCustomAttribute<ExcelColumnAttribute>()!.Order)
                    .ToList();
            SetExcelTitle(mySheet, properties);
            SetExcelBody(mySheet, source, properties);
            using var ms = new MemoryStream();
            myWorkBook.Write(ms);
            ms.Flush();
            return ms.ToArray();
        }
        private static void SetExcelTitle(ISheet sheet, List<PropertyInfo> props)
        {
            IRow title = sheet!.CreateRow(0);
            var attrs = props.Select(p => p.GetCustomAttribute<ExcelColumnAttribute>(false)).ToArray();
            for (int i = 0; i < attrs.Length; i++)
            {
                title.CreateCell(i).SetCellValue(attrs[i]!.Name!.Trim());
            }
        }

        private static void SetExcelBody<T>(ISheet sheet, List<T> source, List<PropertyInfo> props)
        {
            if (source.Count == 0)
                return;
            for (int i = 0; i < source.Count; i++)
            {
                IRow row = sheet.CreateRow(i + 1);
                var obj = source[i];
                for (int j = 0; j < props.Count; j++)
                {
                    var val = props[j]!.GetValue(obj);
                    row.CreateCell(j).SetCellValue(val?.ToString());
                }
            }
        }

        public List<T> Import<T>(byte[] data)
        {
            MemoryStream ms = new(data);
            return Import<T>("unknowe.xlsx", ms);
        }

        private void ReadRow(PropertyInfo[] properties)
        {
            // row
            var propertyDesc = properties!.Select(p => p.GetCustomAttribute<ExcelColumnAttribute>())
                        .ToArray();
            var titleRow = sheet!.GetRow(titleRowIndex);
            columnsIndex = new int[titleRow.LastCellNum];
            for (int i = 0; i < titleRow.LastCellNum; i++)
            {
                ICell cell = titleRow.GetCell(i);
                if (cell == null) continue;
                var excelTitle = cell.ToString()!.Trim();
                columnsIndex[i] = Array.FindIndex(propertyDesc, p => p!.Name!.Trim() == excelTitle);
            }
        }

        private List<T> ReadBody<T>(PropertyInfo[] properties)
        {
            _ = sheet!.GetRow(contentRowIndex);
            var list = new List<T>(sheet.LastRowNum);
            for (int i = contentRowIndex; i <= sheet.LastRowNum; i++)
            {
                IRow row = sheet.GetRow(i);
                if (row == null) continue;
                var obj = Activator.CreateInstance<T>();
                for (int j = 0; j < columnsIndex!.Length; j++)
                {
                    var propertyLocation = columnsIndex[j];
                    if (propertyLocation == -1) continue;

                    //空值忽略
                    var cell = row.GetCell(j);
                    if (cell == null)
                        continue;

                    string value;
                    if (cell.CellType == CellType.Formula)
                    {
                        cell.SetCellType(CellType.String);
                        value = cell.StringCellValue;
                    }
                    else
                    {
                        value = row.GetCell(j).ToString()!;
                    }
                    string str;
                    // 判断是否可空类型
                    if (IsNullable(properties[propertyLocation].PropertyType))
                    {
                        str = Nullable.GetUnderlyingType(properties[propertyLocation].PropertyType)?.FullName!;
                    }
                    else
                    {
                        str = properties[propertyLocation].PropertyType.FullName!;
                    }

                    if (str == "System.String")
                    {
                        properties[propertyLocation].SetValue(obj, value, null);
                    }
                    else if (str == "System.DateTime" && DateTime.TryParse(value, out DateTime pdt))
                    {
                        properties[propertyLocation].SetValue(obj, pdt, null);
                    }
                    else if (str == "System.Boolean" && bool.TryParse(value, out bool b))
                    {
                        properties[propertyLocation].SetValue(obj, b, null);
                    }
                    else if (str == "System.Int16" && short.TryParse(value, out short pi16))
                    {
                        properties[propertyLocation].SetValue(obj, pi16, null);
                    }
                    else if (str == "System.Int32" && int.TryParse(value, out int pi32))
                    {
                        properties[propertyLocation].SetValue(obj, pi32, null);
                    }
                    else if (str == "System.Single" && float.TryParse(value, out float f))
                    {
                        properties[propertyLocation].SetValue(obj, f, null);
                    }
                    else if (str == "System.Double" && double.TryParse(value, out double d))
                    {
                        properties[propertyLocation].SetValue(obj, d, null);
                    }
                    else if (str == "System.Int64" && long.TryParse(value, out long pi64))
                    {
                        properties[propertyLocation].SetValue(obj, pi64, null);
                    }
                    else if (str == "System.Byte" && byte.TryParse(value, out byte pb))
                    {
                        properties[propertyLocation].SetValue(obj, pb, null);
                    }
                    else
                    {
                        properties[propertyLocation].SetValue(obj, null, null);
                    }
                }
                list.Add(obj);
            }
            return list;
        }

        public List<T> Import<T>(string fileName, byte[] data)
        {
            MemoryStream ms = new(data);
            return Import<T>(fileName, ms);
        }

        public List<T> Import<T>(string fileName, Stream stream)
        {
            try
            {
                if (stream == null)
                    throw new ArgumentNullException(nameof(stream));
                stream.Seek(0, SeekOrigin.Begin);
                workbook = fileName.EndsWith(".xls") ? new HSSFWorkbook(stream) : new SXSSFWorkbook(new XSSFWorkbook(stream));
                if (sheetIndex == -1)
                {
                    AutoAnalyzeSheetIndex();
                }
                if (titleRowIndex == -1)
                {
                    throw new InvalidOperationException($"无效操作：请初始化 {nameof(titleRowIndex)} 与 {nameof(contentRowIndex)}，您在解析文件之前应调用方法 InitStartReadRowIndex");
                }
                sheet = workbook.GetSheetAt(sheetIndex);
                var properties = typeof(T).GetProperties()
                            .Where(p => p.CustomAttributes.Any(p => p.AttributeType == typeof(ExcelColumnAttribute)))
                            .ToArray();
                ReadRow(properties);
                var list = ReadBody<T>(properties);
                return list;
            }
            catch
            {
                throw new Exception("模板解析错误，请确认导入的模板格式");
            }
        }

        public ExcelHelper InitSheetIndex(int sheetIndex)
        {
            this.sheetIndex = sheetIndex;
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
            if (workbook == null) throw new ArgumentNullException("文件读取失败");
            var sheetCount = workbook.NumberOfSheets;
            if (sheetCount > 2) throw new ArgumentNullException("模板解析错误，请确认导入的模板格式");

            sheetIndex = sheetCount - 1;
        }

        private static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) != null;
    }
}
