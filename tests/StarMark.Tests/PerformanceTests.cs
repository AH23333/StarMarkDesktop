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

    // 可控抛异常的设置来源：验证 PerformanceSettingsPolicy「读取一律兜底·绝不抛异常」契约。
    private sealed class ThrowingSettings : IPerformanceSettingsSource
    {
        public PerformanceMode Mode;
        public bool ThrowOnMode;
        public bool ThrowOnBudget;
        public bool ThrowOnCache;
        public PerformanceMode LoadPerformanceMode()
            => ThrowOnMode ? throw new InvalidOperationException("mode read boom") : Mode;
        public double LoadCacheBudgetMb()
            => ThrowOnBudget ? throw new InvalidOperationException("budget read boom") : 777.0;
        public int LoadMaxImageCacheCount()
            => ThrowOnCache ? throw new InvalidOperationException("cache read boom") : 777;
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

    // ===== 契约护栏（BU 批·补覆盖，非修缺陷）=====
    // 上方既有用例对 Set 只喂**互异新键**，从未走「更新既有键」分支（LruCache.cs:60-66
    // 的 _order.Remove(旧节点) + 提升最近度），也未测 MaxCount 运行期下调的淘汰与容量下限。
    // 若「更新」漏摘旧节点：_map[key] 指向 fresh 而旧节点仍留在 _order → EvictBeyond 摘到
    // 头部旧节点时 _map.Remove(该 Key) 会误删活键，_map.Count 与 _order.Length 失同步——
    // 现有三例因从不更新键而**测不到**。当前实现正确，这些测跑绿即钉死该不变式防回归。

    [Fact]
    public void LruCache_Set_ExistingKey_ReplacesValueWithoutGrowingCount()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 99);              // 更新既有键：值应替换
        Assert.Equal(1, cache.Count);    // 不得因更新而把同一键计成两份
        Assert.True(cache.TryGet("a", out var v) && v == 99);
    }

    [Fact]
    public void LruCache_Set_ExistingKey_PromotesRecency_SoEvictionDropsOtherKey()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 11);   // 更新 a → a 成为最近使用，b 退为最久未用
        cache.Set("c", 3);    // 超上限：应淘汰 b 而非 a（漏摘旧节点的实现会误逐 a）

        Assert.True(cache.TryGet("a", out var av) && av == 11);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void LruCache_MaxCountSetter_LowerEvictsLeastRecentToFit()
    {
        var cache = new LruCache<string, int>(5);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);
        cache.MaxCount = 2;              // 运行期下调上限（如切省资源模式）

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));   // 最久未用者被淘汰
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void LruCache_MaxCount_ClampedAtLeastOne_ConstructorAndSetter()
    {
        var cache = new LruCache<string, int>(0);   // 构造入参下限
        cache.MaxCount = -5;                        // setter 入参下限
        cache.Set("a", 1);
        cache.Set("b", 2);                          // 容量恒 ≥1 → 仅保最近一个
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("b", out _));
        Assert.False(cache.TryGet("a", out _));
    }

    // ===== 契约护栏（CF 批·补覆盖，非修缺陷）=====
    // 上方既有用例只喂**正常返回**的 FakeSettings，从未走 PerformanceSettingsPolicy 的核心承诺
    // ——`Try`（:53-61）：任一来源读取抛异常须回退默认、绝不把异常透给调用方（缓存/UI 热路径取预算）。
    // 若日后有人把 EffectiveBudgetMb/MaxCacheCount 简化成直连 Provider（丢 Try），既有三例仍跑绿
    // （Custom 正常值照样返回），真实 SettingsStore 读取抛错却会崩调用方——现有覆盖测不到。以下钉死兜底。

    [Fact]
    public void PerformanceSettingsPolicy_ModeReadThrows_FallsBackToBalanced_AndNeverThrows()
    {
        var prev = PerformanceSettingsPolicy.Provider;
        try
        {
            PerformanceSettingsPolicy.Provider = new ThrowingSettings { ThrowOnMode = true };
            // 三重入口皆须兜底为均衡语义、无一处抛。
            Assert.Equal(PerformanceMode.Balanced, PerformanceSettingsPolicy.CurrentMode());
            Assert.Equal(PerformanceSettingsPolicy.BalancedBudgetMb, PerformanceSettingsPolicy.EffectiveBudgetMb());
            Assert.Equal(PerformanceSettingsPolicy.BalancedMaxCacheCount, PerformanceSettingsPolicy.EffectiveMaxCacheCount());
            Assert.False(PerformanceSettingsPolicy.ResourceSaverActive());
        }
        finally { PerformanceSettingsPolicy.Provider = prev; }
    }

    [Fact]
    public void PerformanceSettingsPolicy_CustomBudgetReadThrows_FallsBackToBalancedBudget()
    {
        var prev = PerformanceSettingsPolicy.Provider;
        try
        {
            // 模式=Custom（读得出），仅预算读取抛 → EffectiveBudgetMb 回退均衡，但缓存计数不受牵连。
            PerformanceSettingsPolicy.Provider = new ThrowingSettings { Mode = PerformanceMode.Custom, ThrowOnBudget = true };
            Assert.Equal(PerformanceSettingsPolicy.BalancedBudgetMb, PerformanceSettingsPolicy.EffectiveBudgetMb());
            Assert.Equal(777, PerformanceSettingsPolicy.EffectiveMaxCacheCount()); // 隔离性：另一路径照常
        }
        finally { PerformanceSettingsPolicy.Provider = prev; }
    }

    [Fact]
    public void PerformanceSettingsPolicy_CustomCacheCountReadThrows_FallsBackToBalancedCacheCount()
    {
        var prev = PerformanceSettingsPolicy.Provider;
        try
        {
            PerformanceSettingsPolicy.Provider = new ThrowingSettings { Mode = PerformanceMode.Custom, ThrowOnCache = true };
            Assert.Equal(PerformanceSettingsPolicy.BalancedMaxCacheCount, PerformanceSettingsPolicy.EffectiveMaxCacheCount());
            Assert.Equal(777.0, PerformanceSettingsPolicy.EffectiveBudgetMb()); // 隔离性：预算路径照常
        }
        finally { PerformanceSettingsPolicy.Provider = prev; }
    }

    [Fact]
    public void PerformanceSettingsPolicy_ResourceSaverActive_False_ForBalancedAndCustom()
    {
        var prev = PerformanceSettingsPolicy.Provider;
        try
        {
            PerformanceSettingsPolicy.Provider = new ThrowingSettings { Mode = PerformanceMode.Balanced };
            Assert.False(PerformanceSettingsPolicy.ResourceSaverActive());
            PerformanceSettingsPolicy.Provider = new ThrowingSettings { Mode = PerformanceMode.Custom };
            Assert.False(PerformanceSettingsPolicy.ResourceSaverActive()); // 仅 ResourceSaver 才亮，Custom 不算
        }
        finally { PerformanceSettingsPolicy.Provider = prev; }
    }
}
