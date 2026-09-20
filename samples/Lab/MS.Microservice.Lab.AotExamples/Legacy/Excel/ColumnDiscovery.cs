using System.Reflection;

namespace MS.Microservice.Lab.AotExamples.Legacy.Excel;

// Runnable mechanism example. ExcelHelper.cs.txt preserves the complete replaced implementation.
public static class ColumnDiscovery
{
    public static string[] Headers<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Select(p => p.Name).ToArray();

    public static object?[] Values<T>(T row) => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Select(p => p.GetValue(row)).ToArray();
}
