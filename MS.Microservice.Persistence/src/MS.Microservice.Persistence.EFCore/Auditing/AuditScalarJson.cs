using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MS.Microservice.Persistence.EFCore.Auditing;

internal static class AuditScalarJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static bool Supports(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(decimal) || type == typeof(double) || type == typeof(float)
            || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    }
    internal static void Write(Utf8JsonWriter writer, object? value, int maximumBytes)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text:
                if (text.Length > maximumBytes || Utf8.GetByteCount(text) > maximumBytes)
                    throw new InvalidOperationException("An audit scalar exceeds the record byte budget.");
                writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case Guid id: writer.WriteStringValue(id); break;
            case DateTime date: writer.WriteStringValue(date); break;
            case DateTimeOffset date: writer.WriteStringValue(date); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeOnly time: writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture)); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
            case float number when float.IsFinite(number): writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case Enum enumeration: writer.WriteStringValue(enumeration.ToString()); break;
            default: throw new InvalidOperationException("Audit fields must contain supported finite scalar values.");
        }
    }
}
