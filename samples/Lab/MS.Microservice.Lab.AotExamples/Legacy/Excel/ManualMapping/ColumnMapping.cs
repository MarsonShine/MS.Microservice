namespace MS.Microservice.Lab.AotExamples.Legacy.Excel.ManualMapping;

// Independent teaching implementation: header and getter order are explicit; no production wrapper.
public sealed class ColumnMapping<T>(params (string Header, Func<T, object?> Read)[] columns)
{
    private readonly (string Header, Func<T, object?> Read)[] columns = [.. columns];

    public string[] Headers() => columns.Select(c => c.Header).ToArray();

    public object?[] Values(T row)
    {
        var values = new object?[columns.Length];
        for (int i = 0; i < columns.Length; i++) values[i] = columns[i].Read(row);
        return values;
    }
}
