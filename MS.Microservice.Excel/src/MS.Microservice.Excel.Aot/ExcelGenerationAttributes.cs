using NPOI.SS.UserModel;

namespace MS.Microservice.Excel.Aot;

/// <summary>Requests a generated map on a static partial context class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ExcelSerializableAttribute(Type modelType) : Attribute
{
    public Type ModelType { get; } = modelType;
    public string? Name { get; set; }
    public string? Factory { get; set; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ExcelColumnAttribute : Attribute
{
    public ExcelColumnAttribute() { }
    public ExcelColumnAttribute(string name) => Name = name;
    public string? Name { get; set; }
    public int Order { get; set; } = int.MaxValue;
    public bool Ignore { get; set; }
    public Type? Converter { get; set; }
}

/// <summary>Optional typed conversion for a nonstandard column representation.</summary>
public interface IExcelCellConverter<T>
{
    static abstract bool TryRead(ExcelCellReader reader, out T value);
    static abstract void Write(ICell cell, T value, ICellStyle? dateStyle);
}

/// <summary>Typed static-interface dispatch used by generated column operations.</summary>
public static class ExcelCellConverter
{
    public static bool TryRead<T, TConverter>(ExcelCellReader reader, out T value) where TConverter : IExcelCellConverter<T>
        => TConverter.TryRead(reader, out value);
    public static void Write<T, TConverter>(ICell cell, T value, ICellStyle? dateStyle) where TConverter : IExcelCellConverter<T>
        => TConverter.Write(cell, value, dateStyle);
}
