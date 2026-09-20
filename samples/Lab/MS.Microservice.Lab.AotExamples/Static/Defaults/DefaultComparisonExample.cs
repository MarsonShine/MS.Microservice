namespace MS.Microservice.Lab.AotExamples.Static.Defaults;

public static class DefaultComparisonExample
{
    public static T? GetDefault<T>() => default;
    public static bool IsDefault<T>(T? value) => EqualityComparer<T>.Default.Equals(value, default);
}
