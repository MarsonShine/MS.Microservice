using SqlSugar;

namespace MS.Microservice.Persistence.SqlSugar.Tests;

public sealed class ScopedClientTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void ClientIsCreatedOncePerScopeAndNeverAtRegistration(int scopeCount)
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddSqlSugarClient<SqlSugarClient>(new(), () => new()
        {
            ConnectionString = "Data Source=:memory:", DbType = DbType.Sqlite, IsAutoCloseConnection = true
        }, configuration => { created++; return new SqlSugarClient(configuration); });
        Assert.Equal(0, created);
        using var provider = services.BuildServiceProvider();
        var clients = new List<SqlSugarClient>();
        for (var index = 0; index < scopeCount; index++)
        {
            using var scope = provider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<SqlSugarClient>();
            Assert.Same(client, scope.ServiceProvider.GetRequiredService<SqlSugarClient>());
            Assert.DoesNotContain(clients, previous => ReferenceEquals(previous, client));
            clients.Add(client);
        }
        Assert.Equal(scopeCount, created);
    }

    [Fact]
    public void MissingFactoriesFailAtRegistration()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddSqlSugarClient<SqlSugarClient>(new(), null!, config => new(config)));
        Assert.Throws<ArgumentNullException>(() => services.AddSqlSugarClient<SqlSugarClient>(new(), () => new(), null!));
    }

    [Fact]
    public void GenericAssemblyDoesNotCarryUserDemo()
        => Assert.DoesNotContain(typeof(SqlSugarOptions).Assembly.GetTypes(), type => type.Name.Contains("UserDemo"));
}
