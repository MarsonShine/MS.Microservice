namespace MS.Microservice.Lab.AotExamples.Static.AI;

public static class ProviderSelection
{
    public static bool Matches<T>(T model, string provider, Func<T, string?> getProvider) where T : class =>
        string.Equals(getProvider(model), provider, StringComparison.OrdinalIgnoreCase);
}
