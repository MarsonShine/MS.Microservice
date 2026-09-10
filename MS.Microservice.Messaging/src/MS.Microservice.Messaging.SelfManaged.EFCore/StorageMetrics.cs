using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

internal static class StorageMetrics
{
    public static async Task CaptureAsync(DbContext context, MessagingDiagnostics diagnostics, CancellationToken token)
    {
        var outgoing = await context.Set<OutboxEntry>().Where(x => x.State != OutboxState.Published)
            .GroupBy(x => x.State).Select(group => new { State = group.Key, Count = group.LongCount() }).ToListAsync(token);
        var incoming = await context.Set<InboxEntry>().Where(x => x.State != InboxState.Processed)
            .GroupBy(x => x.State).Select(group => new { State = group.Key, Count = group.LongCount() }).ToListAsync(token);
        foreach (var state in Enum.GetValues<OutboxState>().Where(x => x != OutboxState.Published))
            diagnostics.SetQueueDepth("SelfManaged", "outbox", state.ToString(), outgoing.FirstOrDefault(x => x.State == state)?.Count ?? 0);
        foreach (var state in Enum.GetValues<InboxState>().Where(x => x != InboxState.Processed))
            diagnostics.SetQueueDepth("SelfManaged", "inbox", state.ToString(), incoming.FirstOrDefault(x => x.State == state)?.Count ?? 0);
    }
}
