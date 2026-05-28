namespace MS.Microservice.Core.Functional
{
    public static class ActionExtensions
    {
        extension(Action action)
        {
            public Func<Unit> ToFunc() => () => { action(); return Unit.Default; };
        }

        extension<T>(Action<T> action)
        {
            public Func<T, Unit> ToFunc() => t => { action(t); return Unit.Default; };
        }

        extension<T1, T2>(Action<T1, T2> action)
        {
            public Func<T1, T2, Unit> ToFunc() => (t1, t2) => { action(t1, t2); return Unit.Default; };
        }
    }
}
