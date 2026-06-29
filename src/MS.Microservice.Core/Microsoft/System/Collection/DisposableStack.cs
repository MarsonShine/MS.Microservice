using System;
using System.Collections.Generic;

namespace Microsoft.System.Collection
{
    /// <summary>
    /// 一个专门存放 IDisposable 对象的栈。
    ///
    /// 当 DisposableStack 自身被 Dispose 时，
    /// 会自动释放栈中所有还没有被 Pop 出去的对象。
    ///
    /// 释放顺序遵循栈的后进先出原则：
    /// 最后 Push 进去的对象，会最先被 Dispose。
    /// </summary>
    /// <typeparam name="T">
    /// 栈中元素的类型，必须实现 IDisposable。
    /// </typeparam>
    public class DisposableStack<T> : IDisposable where T : IDisposable
    {
        private readonly Stack<T> _stack = new();

        public int Count => _stack.Count;

        public void Push(T item) => _stack.Push(item);

        public T Pop() => _stack.Pop();

        public void Dispose()
        {
            try
            {
                while (_stack.Count > 0)
                {
                    _stack.Pop().Dispose();
                }
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}
