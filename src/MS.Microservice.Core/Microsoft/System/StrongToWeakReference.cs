using System;
using System.Diagnostics;

namespace MS.Microservice.Core.Microsoft.System
{
    /// <summary>
    /// https://source.dot.net/#System.Net.Http/src/libraries/Common/src/System/StrongToWeakReference.cs
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class StrongToWeakReference<T> : WeakReference where T : class
    {
        private T? _strongRef;
        private DateTime _strongTime;

        /// <summary>Initializes the instance with a strong reference to the specified object.</summary>
        /// <param name="obj">The object to wrap.</param>
        public StrongToWeakReference(T obj) : base(obj)
        {
            Debug.Assert(obj != null, "Expected non-null obj");
            _strongRef = obj;
            _strongTime = DateTime.Now;
        }

        /// <summary>Drops the strong reference to the object, keeping only a weak reference.</summary>
        public void MakeWeak() => _strongRef = null;

        /// <summary>Restores the strong reference, assuming the object hasn't yet been collected.</summary>
        public void MakeStrong()
        {
            _strongRef = WeakTarget;
            Debug.Assert(_strongRef != null, $"Expected non-null {nameof(_strongRef)} after setting");
            if (_strongRef != null)
            {
                _strongTime = DateTime.Now;
            }
        }

        /// <summary>Gets the wrapped object.</summary>
        public new T? Target => _strongRef ?? WeakTarget;

        /// <summary>Gets the wrapped object via its weak reference.</summary>
        private T? WeakTarget => base.Target as T;

        public DateTime StrongTime { get => _strongTime; }
    }
}
