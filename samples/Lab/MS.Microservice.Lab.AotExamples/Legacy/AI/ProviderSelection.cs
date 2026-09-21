namespace MS.Microservice.Lab.AotExamples.Legacy.AI;

public static class ProviderSelection
{
    public static bool Matches<T>(T model, string provider) where T : class
    {
        var providerProperty = typeof(T).GetProperty("Provider");
        var value = providerProperty?.GetValue(model) as string;
        return string.Equals(value, provider, StringComparison.OrdinalIgnoreCase);
    }
}
