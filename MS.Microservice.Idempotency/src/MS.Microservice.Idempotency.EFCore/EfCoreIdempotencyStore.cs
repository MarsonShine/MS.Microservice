using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Idempotency.EFCore;

public enum IdempotencyLookupKind { Missing, Replay, DifferentRequest }

public readonly record struct IdempotencyLookup(IdempotencyLookupKind Kind, IdempotencyResponse? Response);

/// <summary>Stores a response in the caller-owned business transaction; it never commits that transaction.</summary>
public sealed class EfCoreIdempotencyStore<TContext>(TContext context, TimeProvider clock) where TContext : DbContext
{
    public async Task<IdempotencyLookup> FindAsync(IdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureModel();
        var record = await context.Set<IdempotencyRecord>().AsNoTracking().SingleOrDefaultAsync(
            row => row.ScopeHash == request.ScopeHash && row.KeyHash == request.KeyHash, cancellationToken);
        if (record is null) return new(IdempotencyLookupKind.Missing, null);
        if (!string.Equals(record.RequestHash, request.RequestHash, StringComparison.Ordinal))
            return new(IdempotencyLookupKind.DifferentRequest, null);
        if (record.CompletedAtUtcTicks is null || record.StatusCode is null || record.ContentType is null || record.Body is null)
            throw new InvalidOperationException("A committed idempotency record has no completed response.");
        return new(IdempotencyLookupKind.Replay,
            new IdempotencyResponse(record.StatusCode.Value, record.ContentType, record.Body, record.Location));
    }

    /// <summary>
    /// Claims the key before invoking the business operation and completes the response before returning.
    /// The caller must execute this inside its outermost business transaction and let failures roll it back.
    /// </summary>
    public async Task<IdempotencyResponse> ClaimAndExecuteAsync(IdempotencyRequest request, TimeSpan retention,
        Func<CancellationToken, Task<IdempotencyResponse>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);
        EnsureModel();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("An existing caller-owned DbContext transaction is required.");
        if (retention < TimeSpan.FromMinutes(1) || retention > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be between one minute and 30 days.");
        cancellationToken.ThrowIfCancellationRequested();

        var record = new IdempotencyRecord
        {
            ScopeHash = request.ScopeHash,
            KeyHash = request.KeyHash,
            RequestHash = request.RequestHash,
            ExpiresAtUtcTicks = clock.GetUtcNow().Add(retention).UtcDateTime.Ticks
        };
        context.Set<IdempotencyRecord>().Add(record);
        // Flush the unique claim before any business side effect. It remains invisible until the owner commits.
        await context.SaveChangesAsync(cancellationToken);

        var response = await operation(cancellationToken)
            ?? throw new InvalidOperationException("The operation returned no response.");
        record.StatusCode = response.StatusCode;
        record.ContentType = response.ContentType;
        record.Location = response.Location;
        record.Body = response.CopyBody();
        record.CompletedAtUtcTicks = clock.GetUtcNow().UtcDateTime.Ticks;
        await context.SaveChangesAsync(cancellationToken);
        return response;
    }

    /// <summary>Removes committed records after their retention deadline; schedule this outside request transactions.</summary>
    public Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
    {
        EnsureModel();
        var nowTicks = clock.GetUtcNow().UtcDateTime.Ticks;
        return context.Set<IdempotencyRecord>().Where(row => row.ExpiresAtUtcTicks <= nowTicks)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private void EnsureModel()
    {
        if (context.Model.FindEntityType(typeof(IdempotencyRecord)) is null)
            throw new InvalidOperationException("Call AddHttpIdempotency in the business DbContext model.");
    }
}
