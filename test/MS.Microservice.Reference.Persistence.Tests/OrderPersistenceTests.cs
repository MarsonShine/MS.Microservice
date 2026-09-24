using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using Xunit;

namespace MS.Microservice.Reference.Persistence.Tests;

public sealed class OrderPersistenceTests
{
    [Fact]
    public async Task OrderServicePersistsAnOrderAndTheRepositoryCanReadIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseSqlite(connection));
        services.AddReferenceRepositories<SelfManagedReferenceDbContext>();
        services.AddSelfManagedMessaging<SelfManagedReferenceDbContext>(ReferenceMessages.Topology());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
        await database.Database.EnsureCreatedAsync();

        var owner = new AuditActor("https://issuer.example", "owner");
        var created = await scope.ServiceProvider.GetRequiredService<OrderService>()
            .CreateAsync(new CreateOrder("  SKU-1  ", 2), owner);

        Assert.True(created.IsRight);
        Assert.Equal("SKU-1", created.Right.Sku);
        database.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var saved = await repository.GetAsync(created.Right.Id, owner, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(2, saved.Quantity);
        Assert.Equal(created.Right.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Null(await repository.GetAsync(created.Right.Id,
            new AuditActor(owner.Issuer, "another-owner"), CancellationToken.None));
        Assert.Null(await repository.GetAsync(created.Right.Id,
            new AuditActor("https://another-issuer.example", owner.Subject), CancellationToken.None));
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), owner, CancellationToken.None));
    }
}
