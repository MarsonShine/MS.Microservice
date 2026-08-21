using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Web.Application.Orders;
using MS.Microservice.Web.Infrastructure.Extensions;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class InfrastructureProfileConfigurationTests
{
    [Fact]
    public void AddApplicationInfrastructure_Production_DoesNotRegisterSampleOrderServices()
    {
        var services = new ServiceCollection();

        services.AddApplicationInfrastructure(CreateConfiguration("Production"));

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IOrderWorkflowAppService));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IOrderQueryAppService));
    }

    [Fact]
    public void AddApplicationInfrastructure_Sample_RegistersSampleOrderServices()
    {
        var services = new ServiceCollection();

        services.AddApplicationInfrastructure(CreateConfiguration("Sample"));

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOrderWorkflowAppService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOrderQueryAppService));
    }

    [Fact]
    public void AddApplicationInfrastructure_InvalidProfile_FailsFast()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<OptionsValidationException>(
            () => services.AddApplicationInfrastructure(CreateConfiguration("Unknown")));

        Assert.Contains("Production", exception.Failures.Single());
        Assert.Contains("Sample", exception.Failures.Single());
    }

    private static IConfiguration CreateConfiguration(string profile)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:Profile"] = profile,
                ["ConnectionStrings:ActivationConnection"] = "Host=localhost;Database=activation_test;Username=test;Password=test",
                ["ConnectionStrings:EventStoreConnection"] = "Host=localhost;Database=event_store_test;Username=test;Password=test",
                ["ConnectionStrings:Default"] = "Host=localhost;Database=sqlsugar_test;Username=test;Password=test",
                ["FzPlatformDbContextSettings:AutoTimeTracker"] = "Disabled",
                ["FzPlatformDbContextSettings:EnabledSoftDeleted"] = "true",
                ["SqlSugarOptions:IsAutoCloseConnection"] = "true",
                ["SqlSugarOptions:PrintLog"] = "false",
                ["ShardingOptions:ConnectionStrings:0"] = "Host=localhost;Database=sqlsugar_shard_test;Username=test;Password=test",
                ["ShardingOptions:DbType"] = "PostgreSQL",
                ["ShardingOptions:IsAutoCloseConnection"] = "true",
                ["ShardingOptions:PrintLog"] = "false"
            })
            .Build();
}
