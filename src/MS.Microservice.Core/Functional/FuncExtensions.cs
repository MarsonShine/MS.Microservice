namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 提供偏函数应用与函数柯里化支持。
    /// </summary>
    public static partial class FuncExtensions
    {
        extension<T1, T2, TResult>(Func<T1, T2, TResult> func)
        {
            public Func<T1, Func<T2, TResult>> Curry()
                => arg1 => arg2 => func(arg1, arg2);

            public Func<T2, TResult> Apply(T1 arg1)
                => arg2 => func(arg1, arg2);
        }

        extension<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> func)
        {
            public Func<T1, Func<T2, Func<T3, TResult>>> Curry()
                => arg1 => arg2 => arg3 => func(arg1, arg2, arg3);

            public Func<T2, Func<T3, TResult>> Apply(T1 arg1)
                => arg2 => arg3 => func(arg1, arg2, arg3);
        }

        extension<T1, T2, TResult>(Func<T1, Func<T2, TResult>> func)
        {
            public Func<T2, TResult> Apply(T1 arg1)
                => func(arg1);
        }

        extension<T1, T2, T3, TResult>(Func<T1, Func<T2, Func<T3, TResult>>> func)
        {
            public Func<T2, Func<T3, TResult>> Apply(T1 arg1)
                => func(arg1);
        }
    }
}
