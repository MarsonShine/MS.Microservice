using Microsoft.EntityFrameworkCore.Storage;
using MS.Microservice.Persistence.EFCore.DbContext;

namespace MS.Microservice.Persistence.EFCore.Inbox;

public interface IInboxTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IInboxTransactionCoordinator
{
    Task<IInboxTransaction> BeginAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class EfCoreInboxTransactionCoordinator(ActivationDbContext dbContext)
    : IInboxTransactionCoordinator
{
    public async Task<IInboxTransaction> BeginAsync(CancellationToken cancellationToken = default)
        => new EfCoreInboxTransaction(
            await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);

    private sealed class EfCoreInboxTransaction(IDbContextTransaction transaction) : IInboxTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
