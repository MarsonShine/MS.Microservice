namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 提供偏函数应用与函数柯里化支持。
    /// </summary>
    public static partial class FuncExtensions
    {
        extension<T1, T2, TResult>(Func<T1, T2, TResult> func)
        {
            /// <summary>
            /// 将二元函数转换为一连串一元函数，便于逐个提供参数。
            /// </summary>
            public Func<T1, Func<T2, TResult>> Curry()
                => arg1 => arg2 => func(arg1, arg2);

            /// <summary>
            /// 对二元函数先应用第一个参数，返回等待第二个参数的新函数。
            /// </summary>
            public Func<T2, TResult> Apply(T1 arg1)
                => arg2 => func(arg1, arg2);
        }

        extension<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> func)
        {
            /// <summary>
            /// 将三元函数转换为一连串一元函数，便于逐个提供参数。
            /// </summary>
            public Func<T1, Func<T2, Func<T3, TResult>>> Curry()
                => arg1 => arg2 => arg3 => func(arg1, arg2, arg3);

            /// <summary>
            /// 对三元函数先应用第一个参数，返回等待后续参数的新函数。
            /// </summary>
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
