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

    // ─────────── 批次 SA（P-123 第 3 条）：自定义档的范围/默认收成一颗之后的契约 ───────────

    /// <summary>
    /// 读档与落盘<b>共用同一颗</b> <c>Normalize</c>：两端各自夹住，中间原样。
    /// <para>为什么要钉"中间原样"（2048 这一档）：界面曾把上限写成 2048，而仓储允许 4096 ⇒
    /// 判据收口之后，<b>4096 以下任何值都不该被改动</b>；哪天有人把上限调小，这条会先红。</para>
    /// </summary>
    [Theory]
    [InlineData(0.0, 32.0)]            // 存档里的 0 走不到这里（`is > 0` 那道门在前面），这里钉的是"夹到下限"
    [InlineData(-512.0, 32.0)]
    [InlineData(31.9, 32.0)]
    [InlineData(32.0, 32.0)]            // 下限本身不动
    [InlineData(200.0, 200.0)]
    [InlineData(2048.0, 2048.0)]        // 旧界面的上限：在判据的范围里，所以不该被改写
    [InlineData(3000.0, 3000.0)]        // ★ 旧界面表达不出、但仓储认的那个值
    [InlineData(4096.0, 4096.0)]
    [InlineData(99999.0, 4096.0)]
    public void PerformanceTier_BudgetNormalize_ClampsBothEdgesAndPassesTheMiddleThrough(double raw, double expected)
        => Assert.Equal(expected, PerformanceSettingsPolicy.NormalizeBudgetMb(raw));

    [Theory]
    [InlineData(0, 16)]
    [InlineData(15, 16)]
    [InlineData(16, 16)]
    [InlineData(256, 256)]
    [InlineData(1024, 1024)]            // 旧界面的上限
    [InlineData(2000, 2000)]            // ★ 旧界面表达不出、仓储认的值
    [InlineData(4096, 4096)]
    [InlineData(50000, 4096)]
    public void PerformanceTier_CacheCountNormalize_ClampsBothEdgesAndPassesTheMiddleThrough(int raw, int expected)
        => Assert.Equal(expected, PerformanceSettingsPolicy.NormalizeCacheCount(raw));

    /// <summary>范围自身要成立，且默认值落在范围内——否则"没存过"的兜底一落到界面上就是非法值。</summary>
    [Fact]
    public void PerformanceTier_RangesAreOrderedAndDefaultsLieInsideThem()
    {
        Assert.True(PerformanceSettingsPolicy.BudgetMbFloor < PerformanceSettingsPolicy.BudgetMbCeiling);
        Assert.True(PerformanceSettingsPolicy.CacheCountFloor < PerformanceSettingsPolicy.CacheCountCeiling);
        Assert.InRange(PerformanceSettingsPolicy.BudgetMbDefault,
            PerformanceSettingsPolicy.BudgetMbFloor, PerformanceSettingsPolicy.BudgetMbCeiling);
        Assert.InRange(PerformanceSettingsPolicy.CacheCountDefault,
            PerformanceSettingsPolicy.CacheCountFloor, PerformanceSettingsPolicy.CacheCountCeiling);
        // 步进大于 0 且不超过量程：否则滑杆拖到底也够不到上限
        Assert.True(PerformanceSettingsPolicy.BudgetMbStep > 0
            && PerformanceSettingsPolicy.BudgetMbStep < PerformanceSettingsPolicy.BudgetMbCeiling - PerformanceSettingsPolicy.BudgetMbFloor);
        Assert.True(PerformanceSettingsPolicy.CacheCountStep > 0
            && PerformanceSettingsPolicy.CacheCountStep < PerformanceSettingsPolicy.CacheCountCeiling - PerformanceSettingsPolicy.CacheCountFloor);
    }

    /// <summary>
    /// <b>这批真正的收获</b>：每个模式预设都必须落在"自定义"的量程内。
    /// <para>SA 之前这条<b>不成立</b>——省资源/均衡的缓存条数（128 / 512）在旧界面（上限 1024）里还够得着，
    /// 但预算量程两边一个是 32–2048（界面）一个是 32–4096（仓储）：同一个旋钮的两份范围一旦漂移，
    /// "用户选到的"与"程序在用的"就不再是同一个数。钉住这条，漂移当场变红测（#175 同族）。</para>
    /// </summary>
    [Fact]
    public void PerformanceTier_EveryModePresetIsExpressibleInTheCustomRange()
    {
        Assert.InRange(PerformanceSettingsPolicy.BalancedBudgetMb,
            PerformanceSettingsPolicy.BudgetMbFloor, PerformanceSettingsPolicy.BudgetMbCeiling);
        Assert.InRange(PerformanceSettingsPolicy.ResourceSaverBudgetMb,
            PerformanceSettingsPolicy.BudgetMbFloor, PerformanceSettingsPolicy.BudgetMbCeiling);
        Assert.InRange(PerformanceSettingsPolicy.BalancedMaxCacheCount,
            PerformanceSettingsPolicy.CacheCountFloor, PerformanceSettingsPolicy.CacheCountCeiling);
        Assert.InRange(PerformanceSettingsPolicy.ResourceSaverMaxCacheCount,
            PerformanceSettingsPolicy.CacheCountFloor, PerformanceSettingsPolicy.CacheCountCeiling);
    }
}
