using System.Globalization;

namespace MS.Microservice.Infrastructure.Utils.Excel;

/// <summary>Reusable, ordered model mapping. Only explicitly declared columns participate.</summary>
public sealed class ExcelModelMap<T> where T : class
{
    public ExcelModelMap(Func<T> factory, params ExcelColumn<T>[] columns)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(columns);
        Factory = factory;
        Slots = (ExcelColumn<T>[])columns.Clone();
        NameToSlotIndex = new Dictionary<string, int>(columns.Length, StringComparer.Ordinal);
        for (int i = 0; i < Slots.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(Slots[i]);
            NameToSlotIndex[Slots[i].ColumnName] = i;
        }
    }

    internal Func<T> Factory { get; }
    internal ExcelColumn<T>[] Slots { get; }
    internal Dictionary<string, int> NameToSlotIndex { get; }
}

public sealed class ExcelColumn<T> where T : class
{
    private ExcelColumn(string name, Func<T, object?> getter, Action<T, object?> setter,
        TypeCode typeCode, ExcelTryParse<object?> parser)
    {
        ColumnName = name.Trim();
        Getter = getter;
        Setter = setter;
        TargetTypeCode = typeCode;
        Parse = parser;
    }

    public string ColumnName { get; }
    internal Func<T, object?> Getter { get; }
    internal Action<T, object?> Setter { get; }
    internal TypeCode TargetTypeCode { get; }
    internal ExcelTryParse<object?> Parse { get; }

    /// <summary>A null setter declares an export-only column. Invalid/blank input leaves the factory value unchanged.</summary>
    public static ExcelColumn<T> Create<TValue>(string name, Func<T, TValue> getter,
        Action<T, TValue>? setter, ExcelValueConverter<TValue> converter)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(converter);
        return new(name, item => getter(item),
            setter is null ? static (_, _) => { } : (item, value) => setter(item, converter.ReadValue(value)),
            converter.TypeCode, (string text, out object? value) =>
            {
                bool success = converter.Parse(text, out TValue parsed);
                value = parsed;
                return success;
            });
    }
}

public delegate bool ExcelTryParse<T>(string text, out T value);

/// <summary>Text conversion is supplied as a typed delegate, so enum/custom types need no runtime discovery.</summary>
public sealed class ExcelValueConverter<T>(ExcelTryParse<T> parse)
{
    internal TypeCode TypeCode { get; init; } = TypeCode.Object;
    internal ExcelTryParse<T> Parse { get; } = parse ?? throw new ArgumentNullException(nameof(parse));
    internal Func<object?, T> ReadValue { get; init; } = static value => (T)value!;
}

public static class ExcelValueConverters
{
    public static ExcelValueConverter<string?> String { get; } = new(static (string text, out string? value) => { value = text; return true; }) { TypeCode = TypeCode.String };
    public static ExcelValueConverter<bool> Boolean { get; } = new(bool.TryParse) { TypeCode = TypeCode.Boolean };
    public static ExcelValueConverter<byte> Byte { get; } = new(static (string text, out byte value) => byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Byte };
    public static ExcelValueConverter<short> Int16 { get; } = new(static (string text, out short value) => short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Int16 };
    public static ExcelValueConverter<int> Int32 { get; } = new(static (string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Int32 };
    public static ExcelValueConverter<long> Int64 { get; } = new(static (string text, out long value) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Int64 };
    public static ExcelValueConverter<decimal> Decimal { get; } = new(static (string text, out decimal value) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Decimal };
    public static ExcelValueConverter<double> Double { get; } = new(static (string text, out double value) => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Double };
    public static ExcelValueConverter<float> Single { get; } = new(static (string text, out float value) => float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)) { TypeCode = TypeCode.Single };
    public static ExcelValueConverter<Guid> Guid { get; } = new(System.Guid.TryParse);
    public static ExcelValueConverter<DateTime> DateTime { get; } = new(static (string text, out DateTime value) =>
        System.DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value) || System.DateTime.TryParse(text, out value)) { TypeCode = TypeCode.DateTime };

    public static ExcelValueConverter<TEnum> Enum<TEnum>() where TEnum : struct, Enum =>
        new(static (string text, out TEnum value) => System.Enum.TryParse(text, ignoreCase: true, out value))
        {
            // TEnum is statically registered. Its primitive code preserves direct numeric-cell conversion,
            // including nullable enums; this needs no property discovery or dynamically constructed type.
            TypeCode = Type.GetTypeCode(typeof(TEnum))
        };

    public static ExcelValueConverter<T?> Nullable<T>(ExcelValueConverter<T> converter) where T : struct
    {
        ArgumentNullException.ThrowIfNull(converter);
        return new((string text, out T? value) =>
        {
            bool success = converter.Parse(text, out T parsed);
            value = success ? parsed : null;
            return success;
        })
        {
            TypeCode = converter.TypeCode,
            // Numeric cells can contain the enum's boxed integral value. Convert to T before lifting to T?.
            ReadValue = value => value is null ? null : converter.ReadValue(value)
        };
    }
}
