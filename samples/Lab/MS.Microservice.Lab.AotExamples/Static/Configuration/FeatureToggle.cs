namespace MS.Microservice.Lab.AotExamples.Static.Configuration;

public static class FeatureToggle
{
    public static bool Read(string? value, string path)
    {
        if (value is null) return false;
        try { return bool.Parse(value); }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"Failed to convert configuration value at '{path}' to type 'System.Boolean'.", exception);
        }
    }
}
