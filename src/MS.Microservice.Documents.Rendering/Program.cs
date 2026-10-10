namespace MS.Microservice.Documents.Rendering;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is not ["--html-worker"] and not ["--word-worker"])
        {
            Console.Error.WriteLine("This assembly is a library. Its supervised workers require exactly --html-worker or --word-worker.");
            return 64;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The supervised rendering workers currently require Windows.");
            return 69;
        }
        try
        {
            if (args[0] == "--html-worker") await HtmlRenderWorker.RunAsync().ConfigureAwait(false);
            else await WordRenderWorker.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"[..Math.Min(4096, error.GetType().Name.Length + 2 + error.Message.Length)]);
            return error is InvalidDataException ? 3 : 1;
        }
    }
}
