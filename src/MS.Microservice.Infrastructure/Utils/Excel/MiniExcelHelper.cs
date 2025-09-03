using MiniExcelLibs;
using MS.Microservice.Infrastructure.Utils.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS.Microservice.Infrastructure.Utils
{
    /// <summary>
    /// MiniExcel-based Excel helper using MiniExcel's built-in POCO mapping.
    /// 支持 MiniExcelLibs.Attributes.ExcelColumnName 特性进行列名映射。
    /// </summary>
    public class MiniExcelHelper : IExcelImport, IExcelExport
    {
        private string? _sheetName;
        private int _sheetIndex = 0;
        private int _titleRowIndex = 0;   // 0-based: 标题行索引
        private int _contentRowIndex = 1; // 0-based: 数据起始行索引

        public MiniExcelHelper InitSheetIndex(int sheetIndex)
        {
            _sheetIndex = sheetIndex;
            _sheetName = null;
            return this;
        }

        public MiniExcelHelper InitSheetName(string sheetName)
        {
            _sheetName = sheetName ?? throw new ArgumentNullException(nameof(sheetName));
            return this;
        }

        /// <summary>
        /// titleRowIndex：列名所在行索引（0-based）；contentRowIndex：数据起始行索引（0-based）
        /// </summary>
        public MiniExcelHelper InitStartReadRowIndex(int titleRowIndex, int contentRowIndex)
        {
            _titleRowIndex = titleRowIndex;
            _contentRowIndex = contentRowIndex;
            return this;
        }

        // ---------------- Export ----------------
        public byte[] Export<T>(List<T> source, string sheetName)
        {
            using var ms = new MemoryStream();
            ExportToStream(source, sheetName, ms);
            return ms.ToArray();
        }

        public void ExportToStream<T>(List<T> source, string sheetName, Stream destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentNullException(nameof(sheetName));

            // 直接让 MiniExcel 依据 POCO/ExcelColumnName 写出
            var sheets = new Dictionary<string, object> { [sheetName] = source };
            MiniExcel.SaveAs(destination, sheets);
        }

        // ---------------- Import ----------------
        public List<T> Import<T>(byte[] data) where T : class, new()
            => Import<T>(".xlsx", new MemoryStream(data));

        public List<T> Import<T>(string fileName, byte[] data) where T : class, new()
            => Import<T>(fileName, new MemoryStream(data));

        public List<T> Import<T>(string fileName, Stream stream) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (_titleRowIndex < 0 || _contentRowIndex < 0)
                throw new InvalidOperationException("请先调用 InitStartReadRowIndex 初始化 titleRowIndex 与 contentRowIndex。");

            stream = EnsureSeekable(stream);
            stream.Seek(0, SeekOrigin.Begin);

            // 解析 Sheet 名称（优先名称，其次索引）
            string? name = _sheetName;
            if (string.IsNullOrEmpty(name) && _sheetIndex != 0)
            {
                var names = MiniExcel.GetSheetNames(stream).ToList();
                if (_sheetIndex < 0 || _sheetIndex >= names.Count)
                    throw new ArgumentOutOfRangeException(nameof(_sheetIndex), "sheetIndex 超出范围。");
                name = names[_sheetIndex];
                stream.Seek(0, SeekOrigin.Begin);
            }

            // 起始单元格：将指定标题行作为列头（MiniExcel 读取时会把该行当作头）
            var startCell = $"A{_titleRowIndex + 1}";

            // 使用 MiniExcel 内置泛型映射直接读取为 T
            var rows = MiniExcel.Query<T>(stream, sheetName: name, startCell: startCell);

            // 若数据起始行在标题行之后还需要再跳过若干行
            int extraSkip = Math.Max(_contentRowIndex - (_titleRowIndex + 1), 0);
            if (extraSkip > 0) rows = rows.Skip(extraSkip);

            return rows.ToList();
        }

        public IEnumerable<T> ImportAsEnumerable<T>(string fileName, Stream stream) where T : class, new()
        {
            // 为避免外部释放流导致枚举异常，先物化再 yield
            var list = Import<T>(fileName, stream);
            foreach (var item in list) yield return item;
        }

        private static Stream EnsureSeekable(Stream stream)
        {
            if (stream.CanSeek) return stream;
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }
    }
}
