using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.AspNetCore.Hosting.Extension
{
    public static class IHostExtensions
    {
        public static IHost MigrateDbContext<TContext>(this IHost webHost, Action<TContext, IServiceProvider> seeder) 
            where TContext : EntityFrameworkCore.DbContext
        {
            using (var scope = webHost.Services.CreateScope())
            {
                var services = scope.ServiceProvider!;
                var logger = services.GetRequiredService<ILogger<TContext>>();
                var context = services.GetService<TContext>();

                try
                {
                    logger.LogInformation("Migrating database associated with context {DbContextName}", typeof(TContext).Name);
                    InvokeSeeder(seeder!, context!, services);
                    logger.LogInformation("Migrated database associated with context {DbContextName}", typeof(TContext).Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while migrating the database used on context {DbContextName}", typeof(TContext).Name);
                }
            }

            return webHost;
        }

        private static void InvokeSeeder<TContext>(Action<TContext, IServiceProvider> seeder, TContext context, IServiceProvider services)
            where TContext : EntityFrameworkCore.DbContext
        {
            var env = services.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                //context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            }
            seeder(context, services);
        }
    }
}
