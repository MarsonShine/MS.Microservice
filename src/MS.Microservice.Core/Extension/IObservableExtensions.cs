namespace MS.Microservice.Core.Extension;

using MS.Microservice.Core.Functional;
using System.Reactive.Linq;
using static System.Console;
public static class IObservableExtensions
{
    extension<T>(IObservable<T> source)
    {
        public IDisposable Trace(string name) => source.Subscribe(
            onNext: t => WriteLine($"{name} -> {t}"),
            onError: ex => WriteLine($"{name} ERROR: {ex.Message}"),
            onCompleted: () => WriteLine($"{name} END"));

        public (IObservable<T> Passed, IObservable<T> Failed) Partition(Func<T, bool> predicate)
            => (Passed: from t in source where predicate(t) select t,
                Failed: from t in source where !predicate(t) select t);

        public (IObservable<R> Completed, IObservable<Exception> Faulted) Safety<R>(Func<T, Task<R>> f)
            => source.SelectMany((T t) => Observable.FromAsync(() => f(t).Map((Exception ex) => ex, (R r) => F.Exceptional(r))))
                .Partition();

        public IObservable<(T Previous, T Curent)> PairWithPrevious()
            => from first in source
               from second in source.Take(1)
               select (first, second);
    }

    extension<T>(IObservable<Exceptional<T>> excTs)
    {
        public (IObservable<T> Completed, IObservable<Exception> Faulted) Partition()
        {
            var (success, failure) = excTs.Partition(IsSuccess);
            return (
                    Completed: success.Select(ValueOrDefault),
                    Faulted: failure.Select(ExceptionOrDefault)
                   );

            static T ValueOrDefault(Exceptional<T> ex) => ex.Match(exc => default(T), t => t)!;
            static bool IsSuccess(Exceptional<T> ex) => ex.Match(_ => false, _ => true);
            static Exception ExceptionOrDefault(Exceptional<T> ex) => ex.Match(exc => exc, _ => default(Exception))!;
        }
    }

    extension<T>(IObservable<IObservable<T>> source)
    {
        public IObservable<T> MergeAll() => source.SelectMany(x => x);
    }
}
