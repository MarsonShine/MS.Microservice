using System.Collections;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Core.Security.SensitiveData;

public static class SensitiveLogExtensions
{
    /// <summary>Projects a flat DTO before submitting structured values to any ILogger backend.</summary>
    public static void LogSensitive<T>(this ILogger logger, LogLevel level, string eventName, T value) where T : class
    {
        if (!logger.IsEnabled(level)) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var fields = SensitiveValues.Project(value);
        logger.Log(level, new EventId(5100, eventName), new ProtectedState(eventName, fields), null,
            static (state, _) => state.ToString());
    }
    private sealed class ProtectedState(string name, IReadOnlyDictionary<string, object?> fields)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _values =
            [new("EventName", name), .. fields, new("{OriginalFormat}", "{EventName}")];
        public int Count => _values.Length;
        public KeyValuePair<string, object?> this[int index] => _values[index];
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => name + " " + string.Join(", ", fields.Select(field => $"{field.Key}={field.Value}"));
    }
}
