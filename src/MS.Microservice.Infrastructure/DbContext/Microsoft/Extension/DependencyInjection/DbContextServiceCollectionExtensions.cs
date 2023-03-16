using MS.Microservice.Core.Extension;
using MS.Microservice.Infrastructure.DbContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class DbContextServiceCollectionExtensions
    {
        public static void AddEntityFrameworkMySql(this IServiceCollection services, [NotNull] string connectionString)
        {
            if (connectionString.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var serverVersion = ServerVersion.AutoDetect(connectionString);
            services.AddDbContext<ActivationDbContext>(
                dbContextOptions =>
                {
                    dbContextOptions.UseMySql(
                        connectionString,
                        serverVersion,
                        optionBuilder => optionBuilder.MigrationsHistoryTable("__MigrationsHistory")
                    );

                    //// Zack.EFCore.Batch.MySQL.Pomelo 包
                    //// MySQL支持Pomelo.EntityFrameworkCore.MySql这个EF Core Provider，不支持MySQL官方EF Core Provider。
                    //dbContextOptions.UseBatchEF_MySQLPomelo();

                }, contextLifetime: ServiceLifetime.Scoped);
        }
    }
}
