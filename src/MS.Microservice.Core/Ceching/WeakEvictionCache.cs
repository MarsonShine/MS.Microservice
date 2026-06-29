using MS.Microservice.Core.Microsoft.System;

namespace MS.Microservice.Core.Ceching;

/// <summary>
/// 一个带“弱引用驱逐”机制的缓存。
/// 
/// TValue 必须是引用类型，因为弱引用只能用于引用类型对象。
/// TKey 不能为空，因为 Dictionary 的 key 不允许为 null。
/// 
/// 核心思想：
/// 1. 对象刚加入缓存时，使用强引用保存，防止被 GC 回收。
/// 2. 对象被访问时，会重新变成强引用，表示它最近还在使用。
/// 3. 如果对象长时间没有被访问，DoWeakEviction 会把它降级为弱引用。
/// 4. 一旦对象只剩弱引用，GC 可以在合适的时候回收它。
/// 5. 如果对象已经被 GC 回收，DoWeakEviction 会把对应缓存项移除。
/// </summary>
/// <typeparam name="TKey">缓存 key 类型，不能为空。</typeparam>
/// <typeparam name="TValue">缓存 value 类型，必须是引用类型。</typeparam>
public class WeakEvictionCache<TKey, TValue> where TValue : class
    where TKey : notnull
{
    /// <summary>
    /// 一个对象保持强引用的最长时间。
    /// 
    /// 当对象距离上次变成强引用的时间超过这个阈值后，
    /// DoWeakEviction 会尝试将它降级为弱引用。
    /// </summary>
    private readonly TimeSpan _weakEvictionThreshold;

    /// <summary>
    /// 实际存储缓存项的字典。
    /// 
    /// key 是用户传入的缓存 key。
    /// value 是 `StrongToWeakReference<TValue>`，
    /// 它应该是一个可以在强引用和弱引用之间切换的包装类。
    /// </summary>
    private readonly Dictionary<TKey, StrongToWeakReference<TValue>> _items;

    public WeakEvictionCache(TimeSpan weakEvictionThreshold)
    {
        _weakEvictionThreshold = weakEvictionThreshold;
        _items = [];
    }

    /// <summary>
    /// 向缓存中添加一个对象。
    /// 
    /// 注意：
    /// 如果 key 已经存在，Dictionary.Add 会抛出异常。
    /// 如果 value 为 null，也会抛出 ArgumentNullException。
    /// </summary>
    /// <param name="key">缓存 key。</param>
    /// <param name="value">要缓存的对象，不能为 null。</param>
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
