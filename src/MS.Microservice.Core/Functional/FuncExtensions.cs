namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// 提供偏函数应用与函数柯里化支持。
    /// </summary>
    public static partial class F
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

        public static Func<T1, Func<T2, T3, R>> CurryFirst<T1, T2, T3, R>
         (this Func<T1, T2, T3, R> @this) => t1 => (t2, t3) => @this(t1, t2, t3);

        public static Func<T1, Func<T2, T3, T4, R>> CurryFirst<T1, T2, T3, T4, R>
           (this Func<T1, T2, T3, T4, R> @this) => t1 => (t2, t3, t4) => @this(t1, t2, t3, t4);

        public static Func<T1, Func<T2, T3, T4, T5, R>> CurryFirst<T1, T2, T3, T4, T5, R>
           (this Func<T1, T2, T3, T4, T5, R> @this) => t1 => (t2, t3, t4, t5) => @this(t1, t2, t3, t4, t5);

        public static Func<T1, Func<T2, T3, T4, T5, T6, R>> CurryFirst<T1, T2, T3, T4, T5, T6, R>
           (this Func<T1, T2, T3, T4, T5, T6, R> @this) => t1 => (t2, t3, t4, t5, t6) => @this(t1, t2, t3, t4, t5, t6);

        public static Func<T1, Func<T2, T3, T4, T5, T6, T7, R>> CurryFirst<T1, T2, T3, T4, T5, T6, T7, R>
           (this Func<T1, T2, T3, T4, T5, T6, T7, R> @this) => t1 => (t2, t3, t4, t5, t6, t7) => @this(t1, t2, t3, t4, t5, t6, t7);

        public static Func<T1, Func<T2, T3, T4, T5, T6, T7, T8, R>> CurryFirst<T1, T2, T3, T4, T5, T6, T7, T8, R>
           (this Func<T1, T2, T3, T4, T5, T6, T7, T8, R> @this) => t1 => (t2, t3, t4, t5, t6, t7, t8) => @this(t1, t2, t3, t4, t5, t6, t7, t8);

        public static Func<T1, Func<T2, T3, T4, T5, T6, T7, T8, T9, R>> CurryFirst<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>
           (this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, R> @this) => t1 => (t2, t3, t4, t5, t6, t7, t8, t9) => @this(t1, t2, t3, t4, t5, t6, t7, t8, t9);
    }
}
