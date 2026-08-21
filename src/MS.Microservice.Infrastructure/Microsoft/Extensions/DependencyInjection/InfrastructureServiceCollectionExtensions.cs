using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.Domain;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Infrastructure.DependencyInjection;
using MS.Microservice.Infrastructure.Telemetry.Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Infrastructure 模块注册入口。
    /// 每个 Infrastructure 子模块的职责和注册方法汇总于此，作为模块清单。
    /// 后续模块膨胀时可直接从这里拆出独立注册入口，不需改动 Web 层。
    /// </summary>
    public static partial class InfrastructureServiceCollectionExtensions
    {
        extension(IServiceCollection services)
        {
            // -----------------------------------------------------------------------
            // 持久化门面 — EF Core + SqlSugar
            // 实现已迁移到 Persistence 项目，Infrastructure 仅保留兼容转发。
            // -----------------------------------------------------------------------
            public void AddInfrastructurePersistence(IConfiguration configuration)
            {
                services.AddMicroserviceEfCorePersistence(configuration);
                services.AddMicroserviceSqlSugarPersistence(configuration);
            }

            // -----------------------------------------------------------------------
            // 事件溯源 — Postgres Event Store
            // 文件: EventSourcing/EventSourcingServiceCollectionExtensions.cs
            // 提供 EventStoreDbContext、IEventStore、ISnapshotStore 等注册
            // -----------------------------------------------------------------------
            public void AddInfrastructureEventSourcing(IConfiguration configuration)
            {
                var connectionString = GetRequiredConnectionString(configuration, "EventStoreConnection");
                services.AddPostgresEventSourcing(connectionString);
            }

            // -----------------------------------------------------------------------
            // 可观测性 — OpenTelemetry
            // 文件: Telemetry/OpenTelemetryExtensions.cs
            // 提供 Tracing、Console Exporter、OTLP Exporter、ASP.NET Core 监控
            // -----------------------------------------------------------------------
            public void AddInfrastructureTelemetry()
            {
                services.AddMsOpenTelemetry();
            }

            // -----------------------------------------------------------------------
            // 消息派发 — Wolverine
            // -----------------------------------------------------------------------
            public void AddInfrastructureMessaging()
            {
                services.TryAddScoped<IDomainEventDispatcher, WolverineDomainEventDispatcher>();
                services.TryAddScoped<IIntegrationEventPublisher, WolverineIntegrationEventPublisher>();
            }

            // -----------------------------------------------------------------------
            // 默认生产门面：Messaging + EF Core + Telemetry
            // -----------------------------------------------------------------------
            public IServiceCollection AddInfrastructure(IConfiguration configuration)
                => services.AddInfrastructure(configuration, InfrastructureProfile.Production);

            public IServiceCollection AddInfrastructure(
                IConfiguration configuration,
                InfrastructureProfile profile)
            {
                return profile switch
                {
                    InfrastructureProfile.Production => services.AddInfrastructure(
                        configuration,
                        options => options.UseProductionDefaults()),
                    InfrastructureProfile.Sample => services.AddInfrastructure(
                        configuration,
                        options => options.UseSampleDefaults()),
                    _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown infrastructure profile.")
                };
            }

            public IServiceCollection AddInfrastructure(
                IConfiguration configuration,
                Action<InfrastructureModuleOptions> configure)
            {
                ArgumentNullException.ThrowIfNull(configuration);
                ArgumentNullException.ThrowIfNull(configure);

                var options = new InfrastructureModuleOptions();
                configure(options);

                if (options.MessagingEnabled)
                {
                    services.AddInfrastructureMessaging();
                }

                if (options.EfCorePersistenceEnabled)
                {
                    services.AddMicroserviceEfCorePersistence(configuration);
                }

                if (options.SqlSugarPersistenceEnabled)
                {
                    services.AddMicroserviceSqlSugarPersistence(configuration);
                }

                if (options.EventSourcingEnabled)
                {
                    services.AddInfrastructureEventSourcing(configuration);
                }

                if (options.TelemetryEnabled)
                {
                    services.AddInfrastructureTelemetry();
                }

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
}
