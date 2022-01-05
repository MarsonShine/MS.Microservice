#nullable disable

using System;
using System.Runtime.CompilerServices;
/// <summary>
/// https://github.com/dotnet/aspnetcore/tree/73c1a8d8aa890625b9a627cc12da278049ac362a/src/Shared/ObjectMethodExecutor
/// </summary>
namespace MS.Microservice.Core.Reflection.Internal
{
    /// <summary>
    /// 提供一个通用的可等待的结构体<see cref="ObjectMethodExecutor.MethodExecutorAsync"/>，
    /// 无论这个底层的值是否是 System.Task 还是 FSharpAsync 或是应用程序定义的自定义可等待对象
    /// </summary>
    internal readonly struct ObjectMethodExecutorAwaitable
    {
        private readonly object _customAwaitable;
        private readonly Func<object, object> _getAwaiterMethod;
        private readonly Func<object, bool> _isCompletedMethod;
        private readonly Func<object, object> _getResultMethod;
        private readonly Action<object, Action> _onCompletedMethod;
        private readonly Action<object, Action> _unsafeOnCompletedMethod;

        /// <summary>
        /// 性能说明：因为我们需要 customAwaitable 在这里作为一个对象提供，如果这个对象是值类型，这将触发进一步的内存分配（如装箱）。
        /// 我们不能通过将 customAwaitable 泛型化来修复这个问题，因为调用代码通常在编译时并不知道 awaitable/awaiter 的类型。
        /// 
        /// 尽管如此，我们可以通过完全不传递 customAwaitable 来修复这个问题，相反传递一个 func 函数，它直接从目标对象（如 controller 实例），目标方法（如 action 方法信息），以及参数数组映射到下面的 GetAwaiter() 方法中的自定义 awaiter。实际上直到上游代码在 ObjectMethodExecutorAwaitable 实例对象上调用 GetAwaiter，才会调用实际的方法调用（通过延迟调用实际的方法）。
        /// 这个优化目前还没有实现，因为：
        /// [1] 当 awaitable 是一个对象类型时，这没有什么区别，这是目前最常见的场景（如 System.Task<T>）
        /// [2] 会更加复杂，我们需要各种池化对象来追踪那些调用在 GetAwaiter() 中所有的参数数组。
        /// 如果未来会优化 ValueTask<T> 或者其它可等待的值类型，我们会重新考虑这个问题。
        /// </summary>
        /// <param name="customAwaitable"></param>
        /// <param name="getAwaiterMethod"></param>
        /// <param name="isCompletedMethod"></param>
        /// <param name="getResultMethod"></param>
        /// <param name="onCompletedMethod"></param>
        /// <param name="unsafeOnCompletedMethod"></param>
        public ObjectMethodExecutorAwaitable(
            object customAwaitable,
            Func<object,object> getAwaiterMethod,
            Func<object,bool> isCompletedMethod,
            Func<object, object> getResultMethod,
            Action<object, Action> onCompletedMethod,
            Action<object, Action> unsafeOnCompletedMethod)
        {
            _customAwaitable = customAwaitable;
            _getAwaiterMethod = getAwaiterMethod;
            _isCompletedMethod = isCompletedMethod;
            _getResultMethod = getResultMethod;
            _onCompletedMethod = onCompletedMethod;
            _unsafeOnCompletedMethod = unsafeOnCompletedMethod;
        }

        public Awaiter GetAwaiter()
        {
            var customAwaiter = _getAwaiterMethod(_customAwaitable);
            return new Awaiter(_customAwaitable, _isCompletedMethod, _getResultMethod, _onCompletedMethod, _unsafeOnCompletedMethod);
        }

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly object _customAwaiter;
            private readonly Func<object, bool> _isCompletedMethod;
            private readonly Func<object, object> _getResultMethod;
            private readonly Action<object, Action> _onCompletedMethod;
            private readonly Action<object, Action> _unsafeOnCompletedMethod;
            public Awaiter(
                object customAwaiter,
                Func<object, bool> isCompletedMethod,
                Func<object, object> getResultMethod,
                Action<object, Action> onCompletedMethod,
                Action<object, Action> unsafeOnCompletedMethod
                )
            {
                _customAwaiter = customAwaiter;
                _isCompletedMethod = isCompletedMethod;
                _getResultMethod = getResultMethod;
                _onCompletedMethod = onCompletedMethod;
                _unsafeOnCompletedMethod = unsafeOnCompletedMethod;
            }

            public bool IsCompleted => _isCompletedMethod(_customAwaiter);

            public object GetResult() => _getResultMethod(_customAwaiter);

            public void OnCompleted(Action continuation)
            {
                _onCompletedMethod(_customAwaiter, continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                // 如果底层的 awaitable 实现了 ICriticalNotifyCompletion，那么就使用它的 UnsafeOnCompleted
                // 如果没有，就会选择调用它的 OnCompleted
                // 为什么这是安全的：
                // - 实现 ICriticalNotifyCompletion 是说调用者可以选择是否需要保留执行上下文（它通过调用 OnCompleted 发出信号），或者不需要保留（通过调用 UnsafeOnCompleted）。显然，不保存和恢复上下文要快得多，所以我们希望尽可能这样做。
                // - 如果调用者不需要保留执行上下文，那么调用 UnsafeOnCompleted，那么保存它其实也没有坏处 —— 只是浪费一点点成本。如果调用者看到代理实现了 ICriticalNotifyCompletion，但代理选择将调用传递给底层的 awaitable 的 OnCompleted 方法，就会发生这种情况。
                var underlyingMethodToUse = _unsafeOnCompletedMethod ?? _onCompletedMethod;
                underlyingMethodToUse(_customAwaiter, continuation);
            }
        }
    }
}