using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MS.Microservice.Core.Security.SensitiveData;

public static class SensitiveJson
{
    /// <summary>Wraps the existing System.Text.Json resolver with write-only sensitive-property handling.</summary>
    public static void UseSensitiveValues(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(Apply);
    }
    private static void Apply(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        foreach (var member in type.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var classification = SensitiveValues.ClassificationOf(member);
            if (classification is null) continue;
            var name = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? type.Options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
            var property = type.Properties.FirstOrDefault(candidate => candidate.Name == name);
            if (property?.Get is not { } get) continue;
            if (classification == SensitiveKind.Secret)
            { property.Get = static _ => null; property.ShouldSerialize = static (_, _) => false; }
            else
            {
                if (property.PropertyType != typeof(string)) throw new InvalidOperationException("Phone and email classifications require string properties.");
                property.Get = instance => SensitiveValues.Protect((string?)get(instance), classification.Value);
            }
        }
    }
}
