using System.Buffers;
using NPOI.SS.UserModel;

namespace MS.Microservice.Excel.Aot;

public static class ExcelEnumWriter
{
    public static void Write<TEnum>(ICell cell, TEnum value) where TEnum : struct, Enum
    {
        var name = Enum.GetName(value);
        if (name is not null) { cell.SetCellValue(name); return; }
        Span<char> small = stackalloc char[128];
        if (Enum.TryFormat(value, small, out var written))
        {
            cell.SetCellValue(new string(small[..written]));
            return;
        }
        for (var size = 256; ; size = checked(size * 2))
        {
            var buffer = ArrayPool<char>.Shared.Rent(size);
            try
            {
                if (Enum.TryFormat(value, buffer, out written))
                {
                    cell.SetCellValue(new string(buffer, 0, written));
                    return;
                }
            }
            finally { ArrayPool<char>.Shared.Return(buffer); }
        }
    }
}
