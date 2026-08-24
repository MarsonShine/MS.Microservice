using MS.Microservice.Web.Hosting;

public partial class Program
{
    private static Task Main(string[] args) =>
        PlatformWebHost.RunAsync(args, enableLabEndpoints: false);
}
