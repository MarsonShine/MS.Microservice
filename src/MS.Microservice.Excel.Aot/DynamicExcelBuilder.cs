using NPOI.SS.UserModel;
using System.IO.Pipelines;

namespace MS.Microservice.Excel.Aot
{
    public class DynamicExcelBuilder<T>(IWorkbook workbook, ISheet sheetAt, IReadOnlyList<T> source, ExcelModelMap<T> map) where T : class
    {
        private readonly IWorkbook _workbook = workbook;
        private readonly ISheet _sheet = sheetAt;
        private readonly IReadOnlyList<T> _items = source;
        private readonly ExcelModelMap<T> _map = map ?? throw new ArgumentNullException(nameof(map));
        private readonly Dictionary<string, int> _columnMapping = [];
        private ColumnBinding[] _bindings = [];

        public DynamicExcelBuilder<T> InitInsertRow(int titleRowIndex, int startRowIndex)
        {
            IRow titleRow = _sheet.GetRow(titleRowIndex) ?? throw new InvalidOperationException("未找到标题行");
            ReadTitle(titleRow);

            if (_items.Count == 0)
                return this;

            int itemCount = _items.Count;
            _sheet.ShiftRows(startRowIndex, _sheet.LastRowNum + itemCount + 1, itemCount);

            for (int i = startRowIndex; i < startRowIndex + _items.Count; i++)
            {
                IRow contentRow = _sheet.CreateRow(i);
                contentRow.Height = titleRow.Height;

                foreach (ICell cell in titleRow.Cells)
                {
                    ICell contentCell = contentRow.CreateCell(cell.ColumnIndex);
                    contentCell.CellStyle = cell.CellStyle;
                }
            }
            return this;
        }

        private void ReadTitle(IRow titleRow)
        {
            _columnMapping.Clear();
            var titles = titleRow.Cells;
            if (titles.Count == 0)
            {
                for (int columnIndex = 0; columnIndex < _map.Slots.Length; columnIndex++)
                {
                    _columnMapping[_map.Slots[columnIndex].ColumnName] = columnIndex;
                }
            }
            else
            {
                for (int i = 0; i < titles.Count; i++)
                {
                    string title = titles[i].ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(title))
                    {
                        _columnMapping[title] = titles[i].ColumnIndex;
                    }
                }
            }

            var bindings = new List<ColumnBinding>(_map.Slots.Length);
            foreach (var column in _map.Slots)
            {
                if (_columnMapping.TryGetValue(column.ColumnName, out int columnIndex))
                {
                    bindings.Add(new ColumnBinding(column.Write, columnIndex));
                }
            }
            _bindings = bindings.ToArray();
        }

        public DynamicExcelBuilder<T> InsertCellValue(int contentRowIndex)
        {
            if (Items.Count == 0)
            {
                return this;
            }

            for (int i = 0; i < Items.Count; i++)
            {
                int rowIndex = contentRowIndex++;
                IRow row = _sheet.GetRow(rowIndex) ?? _sheet.CreateRow(rowIndex);
                T obj = Items[i];
                foreach (ColumnBinding binding in _bindings)
                {
                    binding.Write(obj, row, binding.ColumnIndex, null);
                }
            }
            return this;
        }

        public void Write(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            _workbook.Write(destination, leaveOpen: true);
        }

        public void Write(PipeWriter destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            using Stream writerStream = destination.AsStream(leaveOpen: true);
            _workbook.Write(writerStream, leaveOpen: true);
            writerStream.Flush();
        }

        public async ValueTask WriteAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            Write(destination);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(PipeWriter destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            using Stream writerStream = destination.AsStream(leaveOpen: true);
            _workbook.Write(writerStream, leaveOpen: true);
            await writerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<byte[]> WriteAsync()
        {
            using MemoryStream stream = new();
            Write(stream);
            return Task.FromResult(stream.ToArray());
        }

        public IWorkbook Workbook { get { return _workbook; } }

        public IReadOnlyList<T> Items { get { return _items; } }

        private readonly record struct ColumnBinding(ExcelColumnWriter<T> Write, int ColumnIndex);
    }
}
