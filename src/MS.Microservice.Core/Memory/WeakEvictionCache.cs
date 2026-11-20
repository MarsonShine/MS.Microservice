using MS.Microservice.Core.Microsoft.System;

namespace MS.Microservice.Core.Memory;

/// <summary>Manually demotes idle entries to weak references. Not thread-safe; callers schedule eviction.</summary>
public class WeakEvictionCache<TKey, TValue> where TValue : class
    where TKey : notnull
{
    private readonly TimeSpan _weakEvictionThreshold;

    private readonly Dictionary<TKey, StrongToWeakReference<TValue>> _items;

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
        if (_items.Count == 0) return;
        var now = DateTime.Now;
        foreach (var (key, reference) in _items)
        {
            if (reference.Target is null)
            {
                // Dictionary.Remove preserves active enumerators on the supported .NET runtime.
                _items.Remove(key);
            }
            else if (now - reference.StrongTime >= _weakEvictionThreshold)
            {
                reference.MakeWeak();
            }
        }
    }
}
