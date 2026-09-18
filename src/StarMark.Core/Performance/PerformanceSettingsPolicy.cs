#nullable enable
using System;
using StarMark.Abstractions;

namespace StarMark.Core.Performance;

/// <summary>
/// 性能模式有效预算（DeskBox 的 <c>PerformanceSettingsPolicy</c> 同款思路）。
/// <para>
/// 把「用户选的模式」翻译成「具体的内存 / 缓存预算」，组件与缓存统一从这里取上限，
/// 由 <see cref="MemoryReclaimer"/> 在超预算时统一回收。读取一律兜底，绝不抛异常。
/// 设置来源通过 <see cref="Provider"/> 注入（默认返回均衡预算，UI 启动时注入真实 SettingsStore）。
/// </para>
/// </summary>
public static class PerformanceSettingsPolicy
{
    /// <summary>均衡模式下的进程工作集预算（MB）。</summary>
    public const double BalancedBudgetMb = 384.0;
    /// <summary>省资源模式下的进程工作集预算（MB）。</summary>
    public const double ResourceSaverBudgetMb = 160.0;
    /// <summary>均衡模式下的有界缓存最大条目数。</summary>
    public const int BalancedMaxCacheCount = 512;
    /// <summary>省资源模式下的有界缓存最大条目数。</summary>
    public const int ResourceSaverMaxCacheCount = 128;

    /// <summary>设置来源（UI 启动时注入 <c>SettingsStore</c>；未注入时回退均衡默认）。</summary>
    public static IPerformanceSettingsSource Provider { get; set; } = new DefaultPerformanceSettingsSource();

    /// <summary>当前性能模式（读取失败回退均衡）。</summary>
    public static PerformanceMode CurrentMode() => Try(() => Provider.LoadPerformanceMode(), PerformanceMode.Balanced);

    /// <summary>进程工作集预算（MB）。超该值 <see cref="MemoryReclaimer"/> 触发回收。</summary>
    public static double EffectiveBudgetMb()
    {
        var mode = CurrentMode();
        if (mode == PerformanceMode.ResourceSaver) return ResourceSaverBudgetMb;
        if (mode == PerformanceMode.Custom) return Try(() => Provider.LoadCacheBudgetMb(), BalancedBudgetMb);
        return BalancedBudgetMb;
    }

    /// <summary>有界缓存的最大条目数（图片 / 头像等）。</summary>
    public static int EffectiveMaxCacheCount()
    {
        var mode = CurrentMode();
        if (mode == PerformanceMode.ResourceSaver) return ResourceSaverMaxCacheCount;
        if (mode == PerformanceMode.Custom) return Try(() => Provider.LoadMaxImageCacheCount(), BalancedMaxCacheCount);
        return BalancedMaxCacheCount;
    }

    /// <summary>省资源模式是否激活（用于关闭非必要动画 / 实时刷新等）。</summary>
    public static bool ResourceSaverActive() => CurrentMode() == PerformanceMode.ResourceSaver;

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex)
        {
            StarLog.Error("读取性能模式预算失败，已回退默认值", ex);
            return fallback;
        }
    }

    private sealed class DefaultPerformanceSettingsSource : IPerformanceSettingsSource
    {
        public PerformanceMode LoadPerformanceMode() => PerformanceMode.Balanced;
        public double LoadCacheBudgetMb() => BalancedBudgetMb;
        public int LoadMaxImageCacheCount() => BalancedMaxCacheCount;
    }
}
