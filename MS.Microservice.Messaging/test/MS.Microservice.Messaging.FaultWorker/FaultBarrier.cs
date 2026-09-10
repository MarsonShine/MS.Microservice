using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Messaging.FaultWorker;

public sealed class FaultBarrier(string? requested)
{
    private int entered;
    public async Task ReachAsync(string phase, Guid profileId, CancellationToken token)
    {
        if (phase != requested || Interlocked.Exchange(ref entered, 1) != 0) return;
        Console.WriteLine($"BARRIER|{phase}|{profileId:D}");
        await Console.Out.FlushAsync(token);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    internal static Guid? ProfileId(DbContext? context, bool consumer)
        => consumer ? context?.ChangeTracker.Entries<ProfileAuditEntry>().Select(x => (Guid?)x.Entity.ProfileId).FirstOrDefault()
            : context?.ChangeTracker.Entries<UserProfile>().Select(x => (Guid?)x.Entity.Id).FirstOrDefault();
}

internal sealed class SaveBarrier(FaultBarrier barrier) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
        CancellationToken cancellationToken = default)
    {
        if (FaultBarrier.ProfileId(data.Context, true) is { } consumed)
            await barrier.ReachAsync("consume-before-commit", consumed, cancellationToken);
        else if (FaultBarrier.ProfileId(data.Context, false) is { } created)
            await barrier.ReachAsync("save-before-commit", created, cancellationToken);
        return result;
    }
}

internal sealed class CommitBarrier(FaultBarrier barrier) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
        CancellationToken cancellationToken = default)
    {
        if (FaultBarrier.ProfileId(data.Context, true) is { } consumed)
            await barrier.ReachAsync("consume-after-commit", consumed, cancellationToken);
        else if (FaultBarrier.ProfileId(data.Context, false) is { } created)
            await barrier.ReachAsync("save-after-commit", created, cancellationToken);
    }
}
