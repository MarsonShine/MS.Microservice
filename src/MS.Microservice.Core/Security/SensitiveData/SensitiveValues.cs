using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MS.Microservice.Core.Reflection;

namespace MS.Microservice.Core.Security.SensitiveData;

public enum SensitiveKind { Phone, Email, Secret }

/// <summary>Classifies an output property; enable the relevant JSON/log/audit adapter to apply it.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SensitiveAttribute : Attribute
{
    public SensitiveAttribute(SensitiveKind kind)
    { if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind)); Kind = kind; }
    public SensitiveKind Kind { get; }
}

public static class SensitiveValues
{
    public const string Redacted = "[REDACTED]";
    private static readonly ConditionalWeakTable<Type, Field[]> Plans = new();

    /// <summary>Masks a supported phone/email; secrets return null and unrecognized formats are fully redacted.</summary>
    public static string? Protect(string? value, SensitiveKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == SensitiveKind.Secret || value is null) return null;
        if (value.Length == 0) return "";
        if (kind == SensitiveKind.Phone)
        {
            var offset = value[0] == '+' ? 1 : 0;
            if (value.Length - offset is < 8 or > 15 || value.AsSpan(offset).ContainsAnyExceptInRange('0', '9')) return Redacted;
            return string.Create(value.Length, value, static (output, source) =>
            { source.AsSpan().CopyTo(output); output.Slice(3, source.Length - 7).Fill('*'); });
        }
        var at = value.IndexOf('@');
        if (value.Length > 320 || at < 1 || at != value.LastIndexOf('@') || at == value.Length - 1
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))) return Redacted;
        var domain = value.AsSpan(at + 1);
        if (domain.IndexOf('.') < 1 || domain[^1] == '.' || !Rune.TryGetRuneAt(value, 0, out var first)) return Redacted;
        foreach (var character in domain)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-') return Redacted;
        return string.Concat(first.ToString(), "***", value.AsSpan(at));
    }

    /// <summary>Creates a flat protected projection for logs or exports, leaving the source object unchanged.</summary>
    /// <remarks>Unmarked scalar properties are retained. Nested objects are omitted; this runtime-accessor path is not AOT compatible.</remarks>
    public static IReadOnlyDictionary<string, object?> Project<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var fields = Plans.GetValue(value.GetType(), BuildPlan);
        var output = new Dictionary<string, object?>(fields.Length, StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field.Kind == SensitiveKind.Secret) continue;
            var raw = field.Get(value);
            output.Add(field.Name, field.Kind is { } kind ? Protect((string?)raw, kind) : raw);
        }
        return output;
    }
    public static SensitiveKind? ClassificationOf(MemberInfo member)
        => (Attribute.GetCustomAttribute(member, typeof(SensitiveAttribute), inherit: true) as SensitiveAttribute)?.Kind;

    private static Field[] BuildPlan(Type type)
    {
        var fields = new List<Field>();
        foreach (var accessor in PropertyAccessors.Get(type))
        {
            var kind = ClassificationOf(accessor.Property);
            if (kind is SensitiveKind.Phone or SensitiveKind.Email && accessor.PropertyType != typeof(string))
                throw new InvalidOperationException("Phone and email classifications require string properties.");
            var scalar = Nullable.GetUnderlyingType(accessor.PropertyType) ?? accessor.PropertyType;
            if (kind == SensitiveKind.Secret || scalar.IsPrimitive || scalar.IsEnum || scalar == typeof(string)
                || scalar == typeof(decimal) || scalar == typeof(Guid) || scalar == typeof(DateTime)
                || scalar == typeof(DateTimeOffset) || scalar == typeof(DateOnly) || scalar == typeof(TimeOnly))
                fields.Add(new(accessor.Name, accessor.GetValue!, kind));
        }
        return fields.ToArray();
    }
    private sealed record Field(string Name, Func<object, object?> Get, SensitiveKind? Kind);
}
