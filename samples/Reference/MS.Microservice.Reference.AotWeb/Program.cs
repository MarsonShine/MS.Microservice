namespace MS.Microservice.Reference.AotWeb;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = AotReferenceHost.CreateBuilder(args);
        AotReferenceHost.AddServices(builder);
        var app = builder.Build();
        AotReferenceHost.MapApplication(app);
        await app.RunAsync();
    }
}
