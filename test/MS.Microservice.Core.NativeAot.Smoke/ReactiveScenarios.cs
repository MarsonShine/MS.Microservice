using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using MS.Microservice.Core.Extension;
using static MS.Microservice.Core.NativeAot.Smoke.FoundationScenarios;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal static class ReactiveScenarios
{
    private static async Task<T[]> Read<T>(IObservable<T> source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await source.ToArray().ToTask(timeout.Token);
    }

    public static async Task PartitionAndMerge()
    {
        var partition = Observable.Range(0, 4).Partition(x => x % 2 == 0);
        Check((await Read(partition.Passed)).SequenceEqual([0, 2]), "Value-type passed partition changed.");
        Check((await Read(partition.Failed)).SequenceEqual([1, 3]), "Value-type failed partition changed.");
        var references = new[] { "", "one" }.ToObservable().Partition(x => x.Length > 0);
        Check((await Read(references.Passed)).SequenceEqual(["one"]) && (await Read(references.Failed)).SequenceEqual([""]), "Reference-type partition changed.");
        var merged = new[] { Observable.Return(1), Observable.Return(2) }.ToObservable().MergeAll();
        Check((await Read(merged)).SequenceEqual([1, 2]), "Inner observables were not merged.");
        Check((await Read(Observable.Empty<IObservable<string>>().MergeAll())).Length == 0, "Empty merge did not complete.");
        var failure = new InvalidOperationException("source failure");
        try { await Read(Observable.Throw<int>(failure).Partition(x => true).Passed); }
        catch (InvalidOperationException caught) when (ReferenceEquals(caught, failure)) { return; }
        throw new InvalidOperationException("Observable source error was not preserved.");
    }

    public static async Task AsyncSafety()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var safe = Observable.Return("input").Safety(_ => release.Task);
        var pending = Read(safe.Completed);
        Check(!pending.IsCompleted, "Async result completed before the supplied task.");
        release.SetResult(7);
        Check((await pending).SequenceEqual([7]), "Async value-type result changed.");
        Check((await Read(safe.Faulted)).Length == 0, "Successful task entered the exception branch.");
        var references = Observable.Return(1).Safety(_ => Task.FromResult("output"));
        Check((await Read(references.Completed)).SequenceEqual(["output"]), "Async reference-type result changed.");
        var failure = new InvalidOperationException("task failure");
        var broken = Observable.Return(1).Safety(_ => Task.FromException<string>(failure));
        var errors = await Read(broken.Faulted);
        Check(errors.Length == 1 && ReferenceEquals(errors[0].GetBaseException(), failure), "Faulted task lost its cause.");
        Check((await Read(broken.Completed)).Length == 0, "Faulted task entered the success branch.");
    }

    public static async Task HotPairsAndTrace()
    {
        using var source = new Subject<int>();
        var pairs = Read(source.PairWithPrevious());
        source.OnNext(1);
        source.OnNext(2);
        source.OnNext(3);
        source.OnCompleted();
        Check((await pairs).SequenceEqual([(1, 2), (2, 3)]), "Pairs from a hot source changed.");
        // Keep Console.Out untouched so native progress and parallel tests retain their output.
        using var traceSource = new Subject<string>();
        Check(!traceSource.HasObservers, "Trace source started with observers.");
        var subscription = traceSource.Trace("native-trace");
        Check(traceSource.HasObservers, "Trace did not subscribe.");
        traceSource.OnNext("value");
        subscription.Dispose();
        Check(!traceSource.HasObservers, "Trace disposal did not unsubscribe.");
        using var complete = Observable.Empty<int>().Trace("native-trace-completed");
        using var error = Observable.Throw<int>(new InvalidOperationException("expected")).Trace("native-trace-error");
    }
}
