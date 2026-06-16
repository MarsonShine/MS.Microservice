using Microsoft.Extensions.Configuration;
using MS.Microservice.Infrastructure.DbContext;
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
            // 持久化 — EF Core (Npgsql)
            // 文件: DbContext/DbContextServiceCollectionExtensions.cs
            // 提供 ActivationDbContext 的 Npgsql 配置
            // -----------------------------------------------------------------------
            public void AddInfrastructurePersistence(IConfiguration configuration)
            {
                services.AddOptions<MsPlatformDbContextSettings>()
                    .Bind(configuration.GetSection(MsPlatformDbContextSettings.SectionName))
                    .Validate(options => options.AutoTimeTracker is "Enabled" or "Disabled", "FzPlatformDbContextSettings:AutoTimeTracker must be Enabled or Disabled.")
                    .ValidateOnStart();

                var connectionString = GetRequiredConnectionString(configuration, "ActivationConnection");
                services.AddEntityFrameworkNpgSql(connectionString);
            }

            // -----------------------------------------------------------------------
            // 事件溯源 — Postgres Event Store
            // 文件: EventSourcing/EventSourcingServiceCollectionExtensions.cs
            // 提供 EventStoreDbContext、IEventStore、ISnapshotStore 等注册
            // -----------------------------------------------------------------------
            public void AddInfrastructureEventSourcing(IConfiguration configuration)
            {
                var connectionString = GetRequiredConnectionString(configuration, "ActivationConnection");
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
            // 一次注册所有 Infrastructure 模块
            // 调用顺序建议：持久化 → 事件溯源 → 可观测性
            // -----------------------------------------------------------------------
            public void AddInfrastructure(IConfiguration configuration)
            {
                services.AddInfrastructurePersistence(configuration);
                services.AddInfrastructureEventSourcing(configuration);
                services.AddInfrastructureTelemetry();
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
