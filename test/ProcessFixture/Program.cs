using System.Text;
namespace ProcessFixture;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        switch (args[0])
        {
            case "echo": Console.Write(args[1]); Console.Error.Write("diagnostic"); return 7;
            case "flood":
                await Task.WhenAll(Console.Out.WriteAsync(new string('x', 100000)), Console.Error.WriteAsync(new string('y', 100000)));
                return 0;
            case "sleep": await Task.Delay(TimeSpan.FromSeconds(60)); return 0;
            case "environment": Console.Write(Environment.GetEnvironmentVariable("RUNNER_TEST")); return 0;
            default: return 1;
        }
    }
}
