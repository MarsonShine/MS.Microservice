using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Infrastructure.DependencyInjection;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Persistence.EFCore.DbContext;
using MS.Microservice.Persistence.SqlSugar.Advance.Sharding;
using MS.Microservice.Persistence.SqlSugar.DbContext;
using Xunit;
using OpenTelemetry.Trace;

namespace MS.Microservice.Infrastructure.Tests.DependencyInjection;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public void AddMicroserviceEfCorePersistence_ShouldCompleteServiceRegistration()
    {
        var services = new ServiceCollection();

        services.AddMicroserviceEfCorePersistence(CreateConfiguration());

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ActivationDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IUserRepository));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ILogRepository));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDomainEventDispatcher));
    }

    [Fact]
    public void AddMicroserviceSqlSugarPersistence_ShouldCompleteServiceRegistration()
    {
        var services = new ServiceCollection();

        services.AddMicroserviceSqlSugarPersistence(CreateConfiguration());

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(UserDemoDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IUserDemoRepository));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IUserHashSplitSqlSugarClientFactory));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IUserSpecificSqlSugarClientProvider));
    }

    [Fact]
    public void AddInfrastructure_DefaultProductionProfile_RegistersOnlyProductionModules()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CreateConfiguration());

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ActivationDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(UserDemoDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(EventStoreDbContext));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDomainEventDispatcher)
            && descriptor.ImplementationType == typeof(WolverineDomainEventDispatcher));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(TracerProvider));
    }

    [Fact]
    public void AddInfrastructure_SampleProfile_RegistersAllSampleModules()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CreateConfiguration(), InfrastructureProfile.Sample);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ActivationDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(UserDemoDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(EventStoreDbContext));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDomainEventDispatcher)
            && descriptor.ImplementationType == typeof(WolverineDomainEventDispatcher));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(TracerProvider));
    }

    [Fact]
    public void AddInfrastructure_CustomMessagingOnly_DoesNotRegisterUnselectedModules()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(
            new ConfigurationBuilder().Build(),
            options => options.UseMessaging());

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDomainEventDispatcher)
            && descriptor.ImplementationType == typeof(WolverineDomainEventDispatcher));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ActivationDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(UserDemoDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(EventStoreDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(TracerProvider));
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivationConnection"] = "Host=localhost;Database=activation_test;Username=test;Password=test",
                ["ConnectionStrings:Default"] = "Host=localhost;Database=sqlsugar_test;Username=test;Password=test",
                ["FzPlatformDbContextSettings:AutoTimeTracker"] = "Disabled",
                ["FzPlatformDbContextSettings:EnabledSoftDeleted"] = "true",
                ["SqlSugarOptions:IsAutoCloseConnection"] = "true",
                ["SqlSugarOptions:PrintLog"] = "false",
                ["ShardingOptions:ConnectionStrings:0"] = "Host=localhost;Database=sqlsugar_shard_test;Username=test;Password=test",
                ["ShardingOptions:DbType"] = "PostgreSQL",
                ["ShardingOptions:IsAutoCloseConnection"] = "true",
                ["ShardingOptions:PrintLog"] = "false",
            })
            .Build();
    }
}
