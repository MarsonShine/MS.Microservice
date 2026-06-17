using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.Core.Extension;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.DbContext;
using MS.Microservice.Persistence.EFCore.Repository;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class EfCorePersistenceServiceCollectionExtensions
    {
        public static IServiceCollection AddMicroserviceEfCorePersistence(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddOptions<MsPlatformDbContextSettings>()
                .Bind(configuration.GetSection(MsPlatformDbContextSettings.SectionName))
                .Validate(options => options.AutoTimeTracker is "Enabled" or "Disabled", "FzPlatformDbContextSettings:AutoTimeTracker must be Enabled or Disabled.")
                .ValidateOnStart();

            services.TryAddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
            services.TryAddScoped<IUserRepository, UserRepository>();
            services.TryAddScoped<ILogRepository, LogRepository>();

            var connectionString = GetRequiredConnectionString(configuration, "ActivationConnection");
            services.AddEntityFrameworkNpgSql(connectionString);
            return services;
        }

        public static IServiceCollection AddEntityFrameworkNpgSql(
            this IServiceCollection services,
            [NotNull] string connectionString)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (connectionString.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            services.AddDbContext<ActivationDbContext>(
                dbContextOptions =>
                {
                    dbContextOptions.UseNpgsql(
                        connectionString,
                        optionBuilder => optionBuilder.MigrationsHistoryTable("__MigrationsHistory")
                    );
                }, contextLifetime: ServiceLifetime.Scoped);

            return services;
        }

        private static string GetRequiredConnectionString(IConfiguration configuration, string name)
        {
            var connectionString = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
            }

            return connectionString;
        }
    }
}
