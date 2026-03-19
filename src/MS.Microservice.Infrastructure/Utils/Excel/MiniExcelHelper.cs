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
    /// ֧�� MiniExcelLibs.Attributes.ExcelColumnName ���Խ�������ӳ�䡣
    /// </summary>
    public class MiniExcelHelper : IExcelImport, IExcelExport
    {
        private string? _sheetName;
        private int _sheetIndex = 0;
        private int _titleRowIndex = 0;   // 0-based: ����������
        private int _contentRowIndex = 1; // 0-based: ������ʼ������

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
        /// titleRowIndex������������������0-based����contentRowIndex��������ʼ��������0-based��
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
            Export((IReadOnlyList<T>)source, sheetName, ms);
            return ms.ToArray();
        }

        public void Export<T>(IReadOnlyList<T> source, string sheetName, Stream destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentNullException(nameof(sheetName));

            // ֱ���� MiniExcel ���� POCO/ExcelColumnName д��
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
                throw new InvalidOperationException("���ȵ��� InitStartReadRowIndex ��ʼ�� titleRowIndex �� contentRowIndex��");

            stream = EnsureSeekable(stream);
            stream.Seek(0, SeekOrigin.Begin);

            // ���� Sheet ���ƣ��������ƣ����������
            string? name = _sheetName;
            if (string.IsNullOrEmpty(name) && _sheetIndex != 0)
            {
                var names = MiniExcel.GetSheetNames(stream).ToList();
                if (_sheetIndex < 0 || _sheetIndex >= names.Count)
                    throw new ArgumentOutOfRangeException(nameof(_sheetIndex), "sheetIndex ������Χ��");
                name = names[_sheetIndex];
                stream.Seek(0, SeekOrigin.Begin);
            }

            // ��ʼ��Ԫ�񣺽�ָ����������Ϊ��ͷ��MiniExcel ��ȡʱ��Ѹ��е���ͷ��
            var startCell = $"A{_titleRowIndex + 1}";

            // ʹ�� MiniExcel ���÷���ӳ��ֱ�Ӷ�ȡΪ T
            var rows = MiniExcel.Query<T>(stream, sheetName: name, startCell: startCell);

            // ��������ʼ���ڱ�����֮����Ҫ������������
            int extraSkip = Math.Max(_contentRowIndex - (_titleRowIndex + 1), 0);
            if (extraSkip > 0) rows = rows.Skip(extraSkip);

            return rows.ToList();
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
