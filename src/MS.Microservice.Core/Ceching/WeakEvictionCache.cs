using MS.Microservice.Core.Microsoft.System;
using System;
using System.Collections.Generic;

namespace MS.Microservice.Core.Ceching
{
    public class WeakEvictionCache<TKey, TValue> where TValue : class
        where TKey : notnull
    {
        private readonly TimeSpan _weakEvictionThreshold;
        private Dictionary<TKey, StrongToWeakReference<TValue>> _items;

        public WeakEvictionCache(TimeSpan weakEvictionThreshold)
        {
            _weakEvictionThreshold = weakEvictionThreshold;
            _items = [];
        }

        public void Add(TKey key, TValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Add(key, new StrongToWeakReference<TValue>(value));
        }

        public bool TryGet(TKey key, out TValue? result)
        {
            result = null;
            if (_items.TryGetValue(key, out var value))
            {
                result = value.Target!;
                if (result != null)
                {
                    // 对象被使用时尝试恢复强引用  
                    value.MakeStrong();
                    return true;
                }
            }
            return false;
        }

        public void DoWeakEviction()
        {
            List<TKey> toRemove = new List<TKey>();
            foreach (var strongToWeakReference in _items)
            {
                var reference = strongToWeakReference.Value;
                var target = reference.Target;
                if (target != null)
                {
                    if (DateTime.Now.Subtract(reference.StrongTime) >= _weakEvictionThreshold)
                    {
                        reference.MakeWeak();
                    }
                }
                else
                {
                    // 清除已失效的弱引用  
                    toRemove.Add(strongToWeakReference.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _items.Remove(key);
            }
        }
    }
}
