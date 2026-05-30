using System.Diagnostics;

namespace MS.Microservice.Infrastructure.Utils.Diagnostics;

/// <summary>
/// Lightweight step-by-step performance probe for Excel import/export operations.
/// Measures elapsed time, allocated bytes, and GC collections per phase.
/// </summary>
internal sealed class PerformanceProbe
{
    private readonly List<PhaseRecord> _phases = [];
    private readonly Stopwatch _stopwatch = new();
    private long _baselineAllocated;
    private int[] _baselineGcCounts = [];
    private bool _started;
    private string? _currentPhase;

    public IReadOnlyList<PhaseRecord> Phases => _phases;

    /// <summary>
    /// Start overall measurement and record baseline.
    /// </summary>
    public PerformanceProbe Start(string? initialPhase = null)
    {
        if (_started) throw new InvalidOperationException("Probe already started.");

        _started = true;
#if NET6_0_OR_GREATER
        _baselineAllocated = GC.GetTotalAllocatedBytes(precise: false);
#endif
        _baselineGcCounts = new int[GC.MaxGeneration + 1];
        for (int i = 0; i <= GC.MaxGeneration; i++)
        {
            _baselineGcCounts[i] = GC.CollectionCount(i);
        }

        _stopwatch.Restart();
        if (initialPhase != null)
        {
            BeginPhase(initialPhase);
        }

        return this;
    }

    /// <summary>
    /// Begin timing a named phase. Ends any previous phase.
    /// </summary>
    public PerformanceProbe BeginPhase(string phaseName)
    {
        EndCurrentPhase();
        _currentPhase = phaseName;
        return this;
    }

    /// <summary>
    /// End the current phase and record its metrics.
    /// </summary>
    public PerformanceProbe EndPhase()
    {
        EndCurrentPhase();
        return this;
    }

    /// <summary>
    /// Stop all measurement and record the final "remainder" phase.
    /// </summary>
    public PerformanceReport Stop()
    {
        EndCurrentPhase();
        _stopwatch.Stop();

        return new PerformanceReport(
            TotalElapsed: _stopwatch.Elapsed,
            Phases: _phases.ToArray(),
            TotalAllocatedBytes: GetCurrentAllocatedBytes() - _baselineAllocated,
            GcCountsByGeneration: SubtractGcCounts(_baselineGcCounts, GetCurrentGcCounts()));
    }

    private void EndCurrentPhase()
    {
        if (_currentPhase == null) return;

        var elapsed = _stopwatch.Elapsed;
        var allocated = GetCurrentAllocatedBytes() - _baselineAllocated;
        var currentGcCounts = GetCurrentGcCounts();

        // Subtract previous phases' cumulative to get delta for this phase
        long prevAllocated = 0;
        TimeSpan prevElapsed = TimeSpan.Zero;
        int[]? prevGcCounts = null;
        if (_phases.Count > 0)
        {
            var last = _phases[^1];
            prevElapsed = last.CumulativeElapsed;
            prevAllocated = last.CumulativeAllocatedBytes;
            prevGcCounts = last.CumulativeGcCounts;
        }

        var phaseGcDeltas = SubtractGcCounts(prevGcCounts ?? _baselineGcCounts, currentGcCounts);

        _phases.Add(new PhaseRecord(
            Name: _currentPhase,
            Elapsed: elapsed - prevElapsed,
            AllocatedBytes: allocated - prevAllocated,
            CumulativeElapsed: elapsed,
            CumulativeAllocatedBytes: allocated,
            CumulativeGcCounts: currentGcCounts,
            GcCountsByGeneration: phaseGcDeltas));

        _currentPhase = null;
    }

    private static int[] SubtractGcCounts(int[] baseline, int[] current)
    {
        var result = new int[current.Length];
        for (int i = 0; i < current.Length; i++)
        {
            result[i] = current[i] - (i < baseline.Length ? baseline[i] : 0);
        }
        return result;
    }

    private long GetCurrentAllocatedBytes()
    {
#if NET6_0_OR_GREATER
        return GC.GetTotalAllocatedBytes(precise: false);
#else
        return 0;
#endif
    }

    private int[] GetCurrentGcCounts()
    {
        var counts = new int[GC.MaxGeneration + 1];
        for (int i = 0; i <= GC.MaxGeneration; i++)
        {
            counts[i] = GC.CollectionCount(i);
        }
        return counts;
    }
}

/// <summary>
/// Recorded metrics for a single phase.
/// </summary>
/// <param name="Name">Phase name.</param>
/// <param name="Elapsed">Wall-clock time for this phase.</param>
/// <param name="AllocatedBytes">Bytes allocated during this phase.</param>
/// <param name="CumulativeElapsed">Cumulative elapsed from start.</param>
/// <param name="CumulativeAllocatedBytes">Cumulative allocated bytes from start.</param>
/// <param name="CumulativeGcCounts">Cumulative GC counts up to and including this phase.</param>
/// <param name="GcCountsByGeneration">GC count deltas for this phase by generation.</param>
internal sealed record PhaseRecord(
    string Name,
    TimeSpan Elapsed,
    long AllocatedBytes,
    TimeSpan CumulativeElapsed,
    long CumulativeAllocatedBytes,
    int[]? CumulativeGcCounts,
    int[]? GcCountsByGeneration);

/// <summary>
/// Final aggregated report from a performance probe.
/// </summary>
/// <param name="TotalElapsed">Total wall-clock time.</param>
/// <param name="Phases">Per-phase records.</param>
/// <param name="TotalAllocatedBytes">Total bytes allocated.</param>
/// <param name="GcCountsByGeneration">GC count deltas by generation for the entire run.</param>
internal sealed record PerformanceReport(
    TimeSpan TotalElapsed,
    PhaseRecord[] Phases,
    long TotalAllocatedBytes,
    int[]? GcCountsByGeneration)
{
    /// <summary>
    /// Format the report as a readable table string.
    /// </summary>
    public string ToTable()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Total Elapsed: {TotalElapsed.TotalMilliseconds:F2} ms | Allocated: {FormatBytes(TotalAllocatedBytes)}");
        if (GcCountsByGeneration is { Length: > 0 })
        {
            sb.Append("GC: ");
            for (int i = 0; i < GcCountsByGeneration.Length; i++)
            {
                if (GcCountsByGeneration[i] > 0)
                    sb.Append($"Gen{i}={GcCountsByGeneration[i]} ");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine($"{"Phase",-35} {"Time(ms)",-12} {"Alloc",-12} {"%Time",-10} {"%Alloc",-10}");
        sb.AppendLine(new string('-', 85));

        foreach (var phase in Phases)
        {
            double pctTime = TotalElapsed.TotalMilliseconds > 0
                ? phase.Elapsed.TotalMilliseconds / TotalElapsed.TotalMilliseconds * 100
                : 0;
            double pctAlloc = TotalAllocatedBytes > 0
                ? (double)phase.AllocatedBytes / TotalAllocatedBytes * 100
                : 0;

            sb.AppendLine($"{phase.Name,-35} {phase.Elapsed.TotalMilliseconds,10:F2}  {FormatBytes(phase.AllocatedBytes),10}  {pctTime,8:F1}%  {pctAlloc,8:F1}%");
        }

        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}
