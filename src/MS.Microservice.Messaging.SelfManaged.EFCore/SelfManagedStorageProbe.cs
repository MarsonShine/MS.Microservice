using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class SelfManagedStorageProbe<TContext>(TContext context) : IMessageStorageProbe where TContext : DbContext
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await context.Set<OutboxEntry>().AsNoTracking().AnyAsync(cancellationToken);
        await context.Set<InboxEntry>().AsNoTracking().AnyAsync(cancellationToken);
    }
}
