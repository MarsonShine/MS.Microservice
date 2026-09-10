using Microsoft.AspNetCore.Builder;
using MS.Microservice.Lab.Hosting;
using MS.Microservice.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class LabMessagingConfigurationTests
{
    [Fact]
    public void DisabledLessonsNeedNoBrokerAndRegisterNoReliableProvider()
    {
        var builder = WebApplication.CreateBuilder();
        LabMessaging.Configure(builder, _ => { });
        Assert.DoesNotContain(builder.Services, x => x.ServiceType == typeof(MessagingProviderRegistration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public void EnabledLessonsRegisterExactlyOneSelectedProvider(string? provider)
    {
        var builder = Builder(provider);
        LabMessaging.Configure(builder, _ => { });
        var registration = Assert.Single(builder.Services, x => x.ServiceType == typeof(MessagingProviderRegistration));
        Assert.Equal(provider ?? "SelfManaged", ((MessagingProviderRegistration)registration.ImplementationInstance!).Name);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("SelfManaged,Wolverine")]
    public void UnknownProvidersFail(string provider)
        => Assert.Throws<ArgumentException>(() => LabMessaging.Configure(Builder(provider), _ => { }));

    [Fact]
    public void ReusingLegacyDatabaseFailsBeforeRegistration()
    {
        var builder = Builder("SelfManaged");
        builder.Configuration["ConnectionStrings:ActivationConnection"] =
            builder.Configuration["ConnectionStrings:LabMessagingDatabase"];
        Assert.Throws<ArgumentException>(() => LabMessaging.Configure(builder, _ => { }));
    }

    private static WebApplicationBuilder Builder(string? provider)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LabMessaging:Enabled"] = "true",
            ["Messaging:Provider"] = provider,
            ["ConnectionStrings:LabMessagingDatabase"] = "Host=localhost;Database=lab_messaging;Username=test;Password=test",
            ["Messaging:RabbitMQ:ConnectionString"] = "amqp://test:test@localhost:5672"
        });
        return builder;
    }
}
