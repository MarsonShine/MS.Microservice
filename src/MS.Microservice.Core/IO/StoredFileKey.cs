namespace MS.Microservice.Core.IO;

/// <summary>Creates and validates portable opaque file keys shared by local and object storage.</summary>
public static class StoredFileKey
{
    public static string Create(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length is < 2 or > 10 || extension[0] != '.'
            || extension.AsSpan(1).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"))
            throw new ArgumentException("Use a dot followed by 1 to 9 ASCII letters or digits.", nameof(extension));
        return Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
    }

    public static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 34 or > 42 || key[32] != '.'
            || key.AsSpan(0, 32).ContainsAnyExcept("0123456789abcdef")
            || key.AsSpan(33).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789"))
            throw new ArgumentException("Use a canonical generated file key with a lowercase extension.", nameof(key));
    }
}
