using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class ActivationDbContextTransactionTests
{
    [Fact]
    public async Task CommitTransactionAsync_WhenSaveAndCommitSucceed_CommitsAndDisposesTransaction()
    {
        var transaction = Substitute.For<IDbContextTransaction>();
        await using var context = CreateContext(transaction);

        await context.CommitTransactionAsync(transaction);

        context.SaveChangesCalls.Should().Be(1);
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        GetCurrentTransaction(context).Should().BeNull();
    }

    [Fact]
    public async Task CommitTransactionAsync_WhenSaveFails_RollsBackAndRethrowsSaveException()
    {
        var saveException = new InvalidOperationException("save failed");
        var transaction = Substitute.For<IDbContextTransaction>();
        await using var context = CreateContext(transaction, saveException);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.CommitTransactionAsync(transaction));

        thrown.Should().BeSameAs(saveException);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        GetCurrentTransaction(context).Should().BeNull();
    }

    [Fact]
    public async Task CommitTransactionAsync_WhenCommitFails_RollsBackAndRethrowsCommitException()
    {
        var commitException = new InvalidOperationException("commit failed");
        var transaction = Substitute.For<IDbContextTransaction>();
        transaction.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(commitException));
        await using var context = CreateContext(transaction);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.CommitTransactionAsync(transaction));

        thrown.Should().BeSameAs(commitException);
        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        GetCurrentTransaction(context).Should().BeNull();
    }

    [Fact]
    public async Task CommitTransactionAsync_WhenRollbackFails_PreservesBothExceptions()
    {
        var commitException = new InvalidOperationException("commit failed");
        var rollbackException = new InvalidOperationException("rollback failed");
        var transaction = Substitute.For<IDbContextTransaction>();
        transaction.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(commitException));
        transaction.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(rollbackException));
        await using var context = CreateContext(transaction);

        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => context.CommitTransactionAsync(transaction));

        thrown.InnerExceptions.Should().ContainInOrder(commitException, rollbackException);
        await transaction.Received(1).DisposeAsync();
        GetCurrentTransaction(context).Should().BeNull();
    }

    private static TestActivationDbContext CreateContext(
        IDbContextTransaction transaction,
        Exception? saveException = null)
    {
        var context = new TestActivationDbContext(saveException);
        CurrentTransactionField.SetValue(context, transaction);
        return context;
    }

    private static IDbContextTransaction? GetCurrentTransaction(ActivationDbContext context)
        => (IDbContextTransaction?)CurrentTransactionField.GetValue(context);

    private static readonly FieldInfo CurrentTransactionField =
        typeof(ActivationDbContext).GetField("_currentTransaction", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ActivationDbContext current transaction field was not found.");

    private sealed class TestActivationDbContext(Exception? saveException = null)
        : ActivationDbContext(
            new DbContextOptionsBuilder<ActivationDbContext>().Options,
            Options.Create(new MsPlatformDbContextSettings()),
            Substitute.For<IDomainEventDispatcher>())
    {
        public int SaveChangesCalls { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return saveException is null
                ? Task.FromResult(1)
                : Task.FromException<int>(saveException);
        }
    }
}
