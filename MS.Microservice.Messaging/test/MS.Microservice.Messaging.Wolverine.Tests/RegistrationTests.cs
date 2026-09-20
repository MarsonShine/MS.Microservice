using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using global::Wolverine;
using JasperFx;
using Xunit;

namespace MS.Microservice.Messaging.Wolverine.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void RegistrationIsExclusiveAndDoesNotInstallSelfManagedWorkers()
    {
        var services = new ServiceCollection();
        services.AddWolverineMessaging<TestContext>(Topology(), Options());
        Assert.Throws<InvalidOperationException>(() => services.AddWolverineMessaging<TestContext>(Topology(), Options()));
        Assert.DoesNotContain(services, x => x.ImplementationType?.FullName?.Contains("SelfManaged", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IMessageReceiver));
        Assert.Equal("Wolverine", Assert.IsType<MessagingProviderRegistration>(
            services.Single(x => x.ServiceType == typeof(MessagingProviderRegistration)).ImplementationInstance).Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void ConfigurationUsesDurabilityAndDisablesRuntimeDdl(int retries)
    {
        var options = new WolverineOptions();
        var settings = Options();
        settings.MaxRetryAttempts = retries;
        WolverineMessagingExtensions.ConfigureWolverineMessaging<TestContext>(options, Topology(), settings);
        Assert.Equal(AutoCreate.None, options.AutoBuildMessageStorageOnStartup);
        Assert.Equal(MessageIdentity.IdAndDestination, options.Durability.MessageIdentity);
        Assert.Equal(TimeSpan.FromDays(30), options.Durability.KeepAfterMessageHandling);
        Assert.False(options.Durability.DeadLetterQueueExpirationEnabled);
        Assert.Contains(options.MetadataRules, rule => rule is IntegrationEventIdentityRule);
        var endpoint = options.Transports.GetOrCreate<ConfirmedRabbitMqTransport>()
            .GetOrCreateEndpoint(new Uri("ms-rabbitmq://exchange/profile.changed.v1"));
        Assert.Equal(global::Wolverine.Configuration.EndpointMode.Durable, endpoint.Mode);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("database")]
    [InlineData("broker")]
    public void InvalidConfigurationFailsBeforeAnyConnection(string invalid)
    {
        var options = Options();
        if (invalid == "schema") options.Schema = "unsafe;schema";
        if (invalid == "database") options.ConnectionString = "";
        if (invalid == "broker") options.BrokerConnectionString = "https://localhost";
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    public static MessageTopology Topology() => new([MessageContract.For<Changed>("profile.changed", TestMessageJsonContext.Default.RegisteredChanged)],
        [MessageSubscription.For<Changed, Handler>("audit")]);
    public static WolverineMessagingOptions Options() => new()
    {
        ConnectionString = "Host=localhost;Database=test;Username=test",
        BrokerConnectionString = "amqp://localhost"
    };
    public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
    public sealed class Handler : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => global::Wolverine.EntityFrameworkCore.WolverineEntityCoreExtensions.MapWolverineEnvelopeStorage(model, "wolverine");
    }
}
