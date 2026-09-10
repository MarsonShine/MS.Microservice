using Microsoft.Extensions.Logging;
using global::Wolverine.Persistence.Durability.DeadLetterManagement;
using global::Wolverine.Runtime;

namespace MS.Microservice.Messaging.Wolverine;

internal sealed class WolverineFailedMessageOperations(IWolverineRuntime runtime, MessageTopology topology,
    ILogger<WolverineFailedMessageOperations> logger) : IFailedMessageOperations
{
    public async Task<IReadOnlyList<FailedMessage>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = await runtime.Storage.DeadLetters.QueryAsync(new DeadLetterEnvelopeQuery { PageSize = limit }, cancellationToken);
        return result.Envelopes.Where(x => !x.Replayable).GroupBy(x => x.Id).Select(group =>
        {
            var entry = group.First();
            var contract = topology.Registry.Contracts.FirstOrDefault(x => $"{x.Name}.v{x.Version}" == entry.MessageType);
            // The native API has sent/scheduled timestamps, not a reliable failure timestamp.
            return new FailedMessage($"wolverine:{entry.Id:N}", entry.Id, contract?.Name ?? entry.MessageType,
                contract?.Version ?? 0, group.Count() == 1 ? entry.ReceivedAt : null, null, entry.ExceptionType);
        }).ToArray();
    }

    public async Task<ReplayResult> ReplayAsync(string failureId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureId);
        if (!failureId.StartsWith("wolverine:", StringComparison.Ordinal)
            || !Guid.TryParseExact(failureId[10..], "N", out var id)) return ReplayResult.NotFound;
        var query = new DeadLetterEnvelopeQuery([id]);
        var matches = await runtime.Storage.DeadLetters.QueryAsync(query, cancellationToken);
        if (matches.Envelopes.Count == 0) return ReplayResult.NotFound;
        if (matches.Envelopes.All(x => x.Replayable) || matches.Envelopes.Any(entry =>
            !topology.Registry.Contracts.Any(contract => $"{contract.Name}.v{contract.Version}" == entry.MessageType)))
            return ReplayResult.InvalidState;
        await runtime.Storage.DeadLetters.ReplayAsync(query, cancellationToken);
        logger.LogInformation("Native message replay accepted for {MessageId}", id);
        return ReplayResult.Accepted;
    }
}
