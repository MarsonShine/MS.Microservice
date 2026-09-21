using NPOI.SS.UserModel;

namespace MS.Microservice.Excel.Aot;

/// <summary>Ordered metadata emitted by the Excel source generator.</summary>
public sealed class ExcelModelMap<T> where T : class
{
    public ExcelModelMap(Func<T> factory, params ExcelColumn<T>[] columns)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(columns);
        Factory = factory;
        Slots = (ExcelColumn<T>[])columns.Clone();
        NameToSlotIndex = new Dictionary<string, int>(columns.Length, StringComparer.Ordinal);
        for (var i = 0; i < Slots.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(Slots[i]);
            NameToSlotIndex.Add(Slots[i].ColumnName, i);
        }
    }

    internal Func<T> Factory { get; }
    internal ExcelColumn<T>[] Slots { get; }
    internal Dictionary<string, int> NameToSlotIndex { get; }
}

public delegate void ExcelColumnWriter<in T>(T model, IRow row, int column, ICellStyle? dateStyle);

/// <summary>Generated operations retain each property's concrete type inside the delegate.</summary>
public sealed class ExcelColumn<T>(string name, ExcelColumnWriter<T> write, Action<T, ExcelCellReader>? read) where T : class
{
    public string ColumnName { get; } = name;
    internal ExcelColumnWriter<T> Write { get; } = write;
    internal Action<T, ExcelCellReader>? Read { get; } = read;
}
