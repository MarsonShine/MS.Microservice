using System;
using System.Collections.Generic;

namespace Microsoft.System.Collection
{
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
