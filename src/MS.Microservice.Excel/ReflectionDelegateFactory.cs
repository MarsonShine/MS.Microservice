using System.Linq.Expressions;
using System.Reflection;

namespace MS.Microservice.Infrastructure.Utils;

/// <summary>
/// Creates strongly-typed delegates for property getters, setters, and object factories,
/// eliminating reflection overhead in hot paths.
/// </summary>
internal static class ReflectionDelegateFactory
{
    /// <summary>
    /// Creates a <see cref="Func{T, TResult}"/> that gets a property value from an object.
    /// The delegate is typed as <c>Func&lt;object, object?&gt;</c> with a cast for the input.
    /// </summary>
    public static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        // (object obj) => (object)((TDeclaring)obj).Property
        var objParam = Expression.Parameter(typeof(object), "obj");
        var castTarget = Expression.Convert(objParam, property.DeclaringType!);
        var propertyAccess = Expression.Property(castTarget, property);
        var convertResult = Expression.Convert(propertyAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(convertResult, objParam).Compile();
    }

    /// <summary>
    /// Creates an <see cref="Action{T, TValue}"/> that sets a property value on an object.
    /// The delegate is typed as <c>Action&lt;object, object?&gt;</c> with casts for both parameters.
    /// </summary>
    public static Action<object, object?> CreateSetter(PropertyInfo property)
    {
        // (object obj, object? value) => ((TDeclaring)obj).Property = (TProp)value
        var objParam = Expression.Parameter(typeof(object), "obj");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var castTarget = Expression.Convert(objParam, property.DeclaringType!);
        var propertyAccess = Expression.Property(castTarget, property);
        var castValue = Expression.Convert(valueParam, property.PropertyType);
        var assign = Expression.Assign(propertyAccess, castValue);
        return Expression.Lambda<Action<object, object?>>(assign, objParam, valueParam).Compile();
    }

    /// <summary>
    /// Creates a <see cref="Func{TResult}"/> that calls the parameterless constructor.
    /// Typed as <c>Func&lt;object&gt;</c>.
    /// </summary>
    public static Func<object> CreateFactory(Type type)
    {
        // () => new T()
        var newExpr = Expression.New(type);
        return Expression.Lambda<Func<object>>(Expression.Convert(newExpr, typeof(object))).Compile();
    }
}
