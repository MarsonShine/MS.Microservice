namespace MS.Microservice.Lab.AotExamples.Legacy.Defaults;

public static class DefaultComparisonExample
{
    public static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    public static bool IsDefault(object? value) => value is null || value.Equals(GetDefault(value.GetType()));
}
