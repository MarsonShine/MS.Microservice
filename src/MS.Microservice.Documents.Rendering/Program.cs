namespace MS.Microservice.Documents.Rendering;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is not ["--html-worker"])
        {
            Console.Error.WriteLine("This assembly is a library. Its supervised HTML worker requires exactly --html-worker.");
            return 64;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The supervised HTML worker currently requires Windows.");
            return 69;
        }
        try
        {
            await HtmlRenderWorker.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"[..Math.Min(4096, error.GetType().Name.Length + 2 + error.Message.Length)]);
            return error is InvalidDataException ? 3 : 1;
        }
    }
}
