#nullable enable
using System.Collections.Generic;

namespace StarMark.Core.Performance;

/// <summary>
/// 有界 LRU 缓存（性能模式 / 内存门禁的承载对象）。
/// <para>
/// 条目数超过 <see cref="MaxCount"/> 时自动淘汰最久未使用项；并实现
/// <see cref="IMemoryReclaimParticipant"/>，在 <see cref="MemoryReclaimer"/> 超预算时被动
/// <see cref="Trim"/> 到 <see cref="PerformanceSettingsPolicy.EffectiveMaxCacheCount"/>。
/// 线程安全（内部加锁），可被后台轮询线程直接调用。
/// </para>
/// </summary>
public sealed class LruCache<TKey, TValue> : IMemoryReclaimParticipant where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly object _lock = new();
    private int _maxCount;

    private sealed class Entry
    {
        public TKey Key { get; }
        public TValue Value { get; }
        public Entry(TKey key, TValue value) { Key = key; Value = value; }
    }

    /// <summary>当前允许的最大条目数（受性能模式影响，可运行时调整）。</summary>
    public int MaxCount
    {
        get { lock (_lock) return _maxCount; }
        set { lock (_lock) { _maxCount = Math.Max(1, value); EvictBeyond(); } }
    }

    public LruCache(int maxCount) => _maxCount = Math.Max(1, maxCount);

    /// <summary>尝试取缓存；命中则把该键提到「最近使用」。</summary>
    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddLast(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>写入 / 更新；超过上限自动淘汰最久未使用项。</summary>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
            }
            var fresh = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddLast(fresh);
            _map[key] = fresh;
            EvictBeyond();
        }
    }

    /// <summary>删除指定键。</summary>
    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _map.Remove(key);
                return true;
            }
        }
        return false;
    }

    public void Clear()
    {
        lock (_lock) { _order.Clear(); _map.Clear(); }
    }

    public int Count { get { lock (_lock) return _map.Count; } }

    /// <inheritdoc />
    public void Trim()
    {
        int target = PerformanceSettingsPolicy.EffectiveMaxCacheCount();
        lock (_lock) { _maxCount = Math.Max(1, target); EvictBeyond(); }
    }

    private void EvictBeyond()
    {
        while (_map.Count > _maxCount && _order.First is { } oldest)
        {
            _order.Remove(oldest);
            _map.Remove(oldest.Value.Key);
        }
    }
}
