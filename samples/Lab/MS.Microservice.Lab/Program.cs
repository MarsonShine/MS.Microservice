using MS.Microservice.Lab.Hosting;

public partial class Program
{
    private static Task Main(string[] args) =>
        PlatformWebHost.RunAsync(args, enableLabEndpoints: true);
}
