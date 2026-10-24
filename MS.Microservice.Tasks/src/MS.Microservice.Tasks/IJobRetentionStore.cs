namespace MS.Microservice.Tasks;

/// <summary>Optional maintenance of terminal jobs and their attempt history within a host-authorized scope.</summary>
public interface IJobRetentionStore
{
    /// <summary>Removes candidates in one terminal state whose finish time is strictly before the cutoff; the cutoff cannot be in the future.</summary>
    /// <returns>The number of jobs actually removed, excluding their attempt records. At most <paramref name="take"/> candidates are selected for conditional deletion.</returns>
    Task<int> PruneAsync(string scope, JobState terminalState, DateTimeOffset finishedBeforeUtc, int take = 100,
        CancellationToken cancellationToken = default);
}
