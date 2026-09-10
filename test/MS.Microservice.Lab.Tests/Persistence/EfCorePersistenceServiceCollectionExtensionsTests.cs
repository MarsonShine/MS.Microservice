namespace MS.Microservice.Persistence.EFCore.Tests;

public class EfCorePersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMicroserviceEfCorePersistence_ShouldThrow_WhenServicesIsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => Microsoft.Extensions.DependencyInjection
            .EfCorePersistenceServiceCollectionExtensions
            .AddMicroserviceEfCorePersistence(null!, configuration);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMicroserviceEfCorePersistence_ShouldThrow_WhenConfigurationIsNull()
    {
        var services = new ServiceCollection();

        var act = () => Microsoft.Extensions.DependencyInjection
            .EfCorePersistenceServiceCollectionExtensions
            .AddMicroserviceEfCorePersistence(services, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddMicroserviceEfCorePersistence_ShouldThrow_WhenConnectionStringMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => Microsoft.Extensions.DependencyInjection
            .EfCorePersistenceServiceCollectionExtensions
            .AddMicroserviceEfCorePersistence(services, configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ActivationConnection*");
    }

    [Fact]
    public void AddEntityFrameworkNpgSql_ShouldThrow_WhenServicesIsNull()
    {
        var act = () => Microsoft.Extensions.DependencyInjection
            .EfCorePersistenceServiceCollectionExtensions
            .AddEntityFrameworkNpgSql(null!, "connection");

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddEntityFrameworkNpgSql_ShouldThrow_WhenConnectionStringIsNull()
    {
        var services = new ServiceCollection();

        var act = () => Microsoft.Extensions.DependencyInjection
            .EfCorePersistenceServiceCollectionExtensions
            .AddEntityFrameworkNpgSql(services, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionString");
    }
}