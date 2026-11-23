using System.Collections.ObjectModel;

namespace MS.Microservice.Threading;

public sealed record BatchItemResult<T>(int Index, T? Value, Exception? Error)
{
    public bool IsSuccess => Error is null;
}

/// <summary>Bounded processing of a stable finite list; item failures are collected in input order.</summary>
public static class BatchProcessing
{
    public static async Task<IReadOnlyList<BatchItemResult<TResult>>> CollectAsync<T, TResult>(
        IReadOnlyList<T> items, Func<T, CancellationToken, ValueTask<TResult>> process,
        int maxConcurrency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        cancellationToken.ThrowIfCancellationRequested();
        int count = items.Count;
        if (count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadOnlyCollection<BatchItemResult<TResult>>.Empty;
        }
        var results = new BatchItemResult<TResult>[count];
        await Parallel.ForAsync(0, count,
            new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    results[index] = new(index, await process(items[index], token).ConfigureAwait(false), null);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    results[index] = new(index, default, error);
                }
            }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(results);
    }
}
