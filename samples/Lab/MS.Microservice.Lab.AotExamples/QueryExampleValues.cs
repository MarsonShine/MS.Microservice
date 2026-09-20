using System.Collections;
using System.Globalization;

namespace MS.Microservice.Lab.AotExamples;

internal static class QueryExampleValues
{
    public static void Append(string name, object? value, ICollection<string> target)
    {
        if (value is null) return;
        if (value is IEnumerable items and not string)
        {
            foreach (var item in items) Append(name, item, target);
            return;
        }
        var text = value switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        target.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text ?? ""));
    }
}
