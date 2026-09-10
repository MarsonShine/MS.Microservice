using MS.Microservice.AspNetCore;

namespace MS.Microservice.Reference.Web;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.CreateBuilder(args);
        ReferenceHost.AddServices(builder);
        var app = builder.Build();
        ReferenceHost.MapApplication(app);
        await app.RunAsync();
    }
}
