using System.Reflection;
using MS.Microservice.Core.Common;

internal static class ResourceCase
{
    public static async Task RunAsync()
    {
        const string prefix = "MS.Microservice.Core.Benchmarks.";
        using var expected = Assembly.GetExecutingAssembly().GetManifestResourceStream(prefix + "Sample.txt")
            ?? throw new InvalidDataException("Missing sample fixture.");
        var expectedBytes = new byte[checked((int)expected.Length)];
        expected.ReadExactly(expectedBytes);

        using var first = await ResourceHelper.EmbeddedResourceReadAsync("Sample.txt");
        using var second = await ResourceHelper.EmbeddedResourceReadAsync("Sample.txt");
        using var actual = new MemoryStream();
        await first.CopyToAsync(actual);
        if (actual.Length == 0 || !actual.ToArray().AsSpan().SequenceEqual(expectedBytes)
            || second.Position != 0 || second.ReadByte() != expectedBytes[0])
            throw new InvalidDataException("Resource contents or cursor ownership changed.");

        var missing = ResourceHelper.EmbeddedResourceReadAsync("Missing.txt");
        try
        {
            using var unexpected = await missing;
            throw new InvalidDataException("Missing resource was accepted.");
        }
        catch (ArgumentException error) when (error.Message == $"Resource '{prefix}Missing.txt' not found.")
        {
            Console.WriteLine($"Resource OK: {expectedBytes.Length} bytes, independent cursors, faulted missing lookup.");
        }
    }
}
