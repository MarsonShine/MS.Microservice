namespace MS.Microservice.Lab.AotExamples.Legacy.Excel.ManualMapping;

// Bind only declared columns to the template's real indexes. Execute the getters for each current row.
public static class TemplateMapping
{
    public static (int Index, object? Value)[] BindRow<T>(T row,
        IReadOnlyDictionary<string, int> headers,
        params (string Header, Func<T, object?> Read)[] columns)
    {
        var cells = new List<(int, object?)>(columns.Length);
        foreach (var column in columns)
            if (headers.TryGetValue(column.Header, out int index)) cells.Add((index, column.Read(row)));
        return cells.ToArray();
    }
}
