using Autofac.Extensions.DependencyInjection;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Extension;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            //host.MigrateDbContext<ActivationDbContext>((context, services) =>
            //{
            //    IWebHostEnvironment env = services.GetRequiredService<IWebHostEnvironment>();
            //    var logger = services.GetService<ILogger<ActivationDbContext>>();

            //    new ActivationDbContextSeed().SeedAsync(context, env, logger)
            //    .Wait();
            //});

            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                ;
    }
}