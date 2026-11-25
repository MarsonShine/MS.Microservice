using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;

namespace MS.Microservice.Idempotency.EFCore;

public enum IdempotencyLookupKind { Missing, Replay, DifferentRequest }

public readonly record struct IdempotencyLookup(IdempotencyLookupKind Kind, IdempotencyResponse? Response);

/// <summary>No committed record exists for this operation, actor and key.</summary>
public sealed class IdempotencyMissing
{
    internal static IdempotencyMissing Instance { get; } = new();
    private IdempotencyMissing() { }
}

/// <summary>The key belongs to a request with a different representation.</summary>
public sealed class IdempotencyDifferentRequest
{
    internal static IdempotencyDifferentRequest Instance { get; } = new();
    private IdempotencyDifferentRequest() { }
}

/// <summary>A missing key, a replayable response, or a key used for a different request.</summary>
public union IdempotencyLookupResult(IdempotencyMissing, IdempotencyResponse, IdempotencyDifferentRequest)
{
    /// <summary>Maps this result to the original lookup contract.</summary>
    public IdempotencyLookup ToLegacyLookup() => this switch
    {
        IdempotencyMissing => new(IdempotencyLookupKind.Missing, null),
        IdempotencyResponse response => new(IdempotencyLookupKind.Replay, response),
        IdempotencyDifferentRequest => new(IdempotencyLookupKind.DifferentRequest, null),
        null => throw new InvalidOperationException("An uninitialized idempotency lookup has no result.")
    };
}

/// <summary>Stores a response in the caller-owned business transaction; it never commits that transaction.</summary>
public sealed class EfCoreIdempotencyStore<TContext>(TContext context, TimeProvider clock) where TContext : DbContext
{
    private sealed record StoredResponse(string RequestHash, long? CompletedAtUtcTicks, int? StatusCode,
        string? ContentType, string? Location, byte[]? Body);

    private static readonly Expression<Func<TContext, string, string, string, CancellationToken, StoredResponse?>> Lookup =
        (db, scope, key, fingerprint, token) => db.Set<IdempotencyRecord>().AsNoTracking()
            .Where(row => row.ScopeHash == scope && row.KeyHash == key)
            .Select(row => new StoredResponse(row.RequestHash, row.CompletedAtUtcTicks, row.StatusCode,
                row.ContentType, row.Location, row.RequestHash == fingerprint ? row.Body : null))
            .SingleOrDefault();

    /// <summary>Returns a typed result for the request key without changing the caller's transaction.</summary>
    public async Task<IdempotencyLookupResult> FindResultAsync(IdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureModel();
        // Use the host provider's cache and its model/options key, just as ordinary LINQ queries do.
        var key = context.GetService<ICompiledQueryCacheKeyGenerator>().GenerateCacheKey(Lookup, async: true);
        var query = context.GetService<IMemoryCache>().GetOrCreate((typeof(StoredResponse), key), static entry =>
        {
            entry.SetSize(10); // EF's default cache accounts for a compiled query as 10 units.
            return EF.CompileAsyncQuery(Lookup);
        })!;
        var record = await query(context, request.ScopeHash, request.KeyHash, request.RequestHash, cancellationToken);
        if (record is null) return IdempotencyMissing.Instance;
        // A database collation may consider two distinct hash strings equal.
        if (!string.Equals(record.RequestHash, request.RequestHash, StringComparison.Ordinal))
        {
            return IdempotencyDifferentRequest.Instance;
        }
        if (record.CompletedAtUtcTicks is null || record.StatusCode is null || record.Body is null)
            throw new InvalidOperationException("A committed idempotency record has no completed response.");
        return new IdempotencyResponse(record.StatusCode.Value, record.ContentType, record.Body, record.Location);
    }

    /// <summary>Returns a lookup using the original enum-and-response contract.</summary>
    public async Task<IdempotencyLookup> FindAsync(IdempotencyRequest request,
        CancellationToken cancellationToken = default)
        => (await FindResultAsync(request, cancellationToken)).ToLegacyLookup();

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
