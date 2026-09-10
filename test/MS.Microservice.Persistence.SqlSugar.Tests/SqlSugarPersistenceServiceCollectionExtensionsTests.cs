namespace MS.Microservice.Persistence.SqlSugar.Tests;

public class SqlSugarPersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMicroserviceSqlSugarPersistence_ShouldThrow_WhenServicesIsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => Microsoft.Extensions.DependencyInjection
            .SqlSugarPersistenceServiceCollectionExtensions
            .AddMicroserviceSqlSugarPersistence(null!, configuration);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMicroserviceSqlSugarPersistence_ShouldThrow_WhenConfigurationIsNull()
    {
        var services = new ServiceCollection();

        var act = () => Microsoft.Extensions.DependencyInjection
            .SqlSugarPersistenceServiceCollectionExtensions
            .AddMicroserviceSqlSugarPersistence(services, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddSqlSugarRepository_ShouldThrow_WhenServicesIsNull()
    {
        var act = () => Microsoft.Extensions.DependencyInjection
            .SqlSugarPersistenceServiceCollectionExtensions
            .AddSqlSugarRepository(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}