using System.Reflection;
using System.Runtime.CompilerServices;

namespace MS.Microservice.Infrastructure.Utils;

// The legacy Excel metadata cache owns these delegates. Keep its dynamic discovery
// local to the compatibility implementation instead of restoring it in Core.
internal static class ExcelPropertyAccessors
{
    internal static IEnumerable<Accessor> Get(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0) continue;
            var writable = property.SetMethod?.IsPublic == true
                && !property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
            yield return new(property, property.Name, property.PropertyType,
                ReflectionDelegateFactory.CreateGetter(property),
                writable ? ReflectionDelegateFactory.CreateSetter(property) : null);
        }
    }

    internal static Func<object>? Factory(Type type) => type.GetConstructor(Type.EmptyTypes) is null
        ? null : ReflectionDelegateFactory.CreateFactory(type);

    internal sealed record Accessor(PropertyInfo Property, string Name, Type PropertyType,
        Func<object, object?> GetValue, Action<object, object?>? SetValue);
}
