using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using NSubstitute;
using Xunit;

namespace MS.Microservice.Reference.Application.Tests;

public sealed class UnionBoundaryTests
{
    [Fact]
    public async Task ProfileOperationsRejectAnUninitializedTransactionResult()
    {
        var service = new ProfileService(Substitute.For<IProfileRepository>(), new DefaultUnit(),
            Substitute.For<IIntegrationEventPublisher>(), TimeProvider.System);
        var actor = new AuditActor("https://issuer.example", "admin");

        var create = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new("https://issuer.example", "subject", "name", []), actor));
        var change = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeAsync(Guid.NewGuid(), new("name", [], 1), actor));

        Assert.Equal("A profile operation returned no result.", create.Message);
        Assert.Equal(create.Message, change.Message);
    }

    [Fact]
    public async Task OrderOperationRejectsAnUninitializedTransactionResult()
    {
        var service = new OrderService(Substitute.For<IOrderRepository>(), new DefaultUnit(), TimeProvider.System);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new("SKU", 1), new("https://issuer.example", "owner")));
        Assert.Equal("An order operation returned no result.", error.Message);
    }

    private sealed class DefaultUnit : IUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => Task.FromResult(default(T)!);
    }
}
