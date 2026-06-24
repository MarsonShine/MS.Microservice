using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Persistence.EFCore.DbContext;
using MS.Microservice.Persistence.SqlSugar.Advance.Sharding;
using MS.Microservice.Persistence.SqlSugar.DbContext;
using Xunit;

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
    public void AddInfrastructure_ShouldRemainStableFacadeForPersistenceRegistration()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CreateConfiguration());

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ActivationDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(UserDemoDbContext));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDomainEventDispatcher)
            && descriptor.ImplementationType == typeof(WolverineDomainEventDispatcher));
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
