#nullable enable
using System;
using Xunit;
using StarMark.Core.Performance;

namespace StarMark.Tests;

/// <summary>
/// Phase B-8：性能模式 / 内存门禁 逻辑测试。
/// 覆盖有界 LRU 缓存淘汰、MemoryReclaimer 参与者回收、PerformanceSettingsPolicy 预算换算。
/// </summary>
public sealed class PerformanceTests
{
    private sealed class FakeSettings : IPerformanceSettingsSource
    {
        private readonly PerformanceMode _mode;
        private readonly double _budgetMb;
        private readonly int _maxCache;
        public FakeSettings(PerformanceMode mode, double budgetMb, int maxCache)
            => (_mode, _budgetMb, _maxCache) = (mode, budgetMb, maxCache);
        public PerformanceMode LoadPerformanceMode() => _mode;
        public double LoadCacheBudgetMb() => _budgetMb;
        public int LoadMaxImageCacheCount() => _maxCache;
    }

    private sealed class FakeParticipant : IMemoryReclaimParticipant
    {
        public int TrimCalls { get; private set; }
        public void Trim() => TrimCalls++;
    }

    [Fact]
    public void LruCache_EvictsLeastRecentlyUsed_WhenOverMax()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // 超过上限，a 应被淘汰

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out var b) && b == 2);
        Assert.True(cache.TryGet("c", out var c) && c == 3);
    }

    [Fact]
    public void LruCache_TryGet_PromotesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        // 访问 a，使其成为最近使用；再插入 c 时 b 应被淘汰而非 a
        Assert.True(cache.TryGet("a", out _));
        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void LruCache_Trim_RespectsEffectiveMaxCacheCount()
    {
        // 自定义模式：MaxImageCacheCount = 2
        PerformanceSettingsPolicy.Provider = new FakeSettings(PerformanceMode.Custom, 512, 2);
        var cache = new LruCache<string, int>(50);
        for (int i = 0; i < 10; i++) cache.Set("k" + i, i);

        Assert.Equal(10, cache.Count); // 尚未 Trim
        cache.Trim();                  // 应被 PerformanceSettingsPolicy 压到 2
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void MemoryReclaimer_Trim_NotifiesRegisteredParticipants()
    {
        var p = new FakeParticipant();
        MemoryReclaimer.Default.Register(p);
        MemoryReclaimer.Default.Trim();
        Assert.Equal(1, p.TrimCalls);
    }

    [Fact]
    public void PerformanceSettingsPolicy_CustomBudget_UsesProvider()
    {
        PerformanceSettingsPolicy.Provider = new FakeSettings(PerformanceMode.Custom, 512, 64);
        Assert.Equal(PerformanceMode.Custom, PerformanceSettingsPolicy.CurrentMode());
        Assert.Equal(512.0, PerformanceSettingsPolicy.EffectiveBudgetMb());
        Assert.Equal(64, PerformanceSettingsPolicy.EffectiveMaxCacheCount());
    }

    [Fact]
    public void PerformanceSettingsPolicy_BalancedBudget_IsDefault()
    {
        PerformanceSettingsPolicy.Provider = new FakeSettings(PerformanceMode.Balanced, 0, 0);
        Assert.Equal(PerformanceMode.Balanced, PerformanceSettingsPolicy.CurrentMode());
        Assert.Equal(PerformanceSettingsPolicy.BalancedBudgetMb, PerformanceSettingsPolicy.EffectiveBudgetMb());
        Assert.Equal(PerformanceSettingsPolicy.BalancedMaxCacheCount, PerformanceSettingsPolicy.EffectiveMaxCacheCount());
    }

    [Fact]
    public void PerformanceSettingsPolicy_ResourceSaver_UsesSaverBudget()
    {
        PerformanceSettingsPolicy.Provider = new FakeSettings(PerformanceMode.ResourceSaver, 9999, 9999);
        Assert.Equal(PerformanceSettingsPolicy.ResourceSaverBudgetMb, PerformanceSettingsPolicy.EffectiveBudgetMb());
        Assert.Equal(PerformanceSettingsPolicy.ResourceSaverMaxCacheCount, PerformanceSettingsPolicy.EffectiveMaxCacheCount());
        Assert.True(PerformanceSettingsPolicy.ResourceSaverActive());
    }
}
