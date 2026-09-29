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

    // ─────────────── 自定义档的范围与默认（批次 SA，P-123 第 3 条） ───────────────

    /// <summary>
    /// 「自定义」那两个旋钮的<b>范围、步进与默认值</b>——这四个主人曾是同一件事的四份抄本：
    /// 仓储的 <c>Math.Clamp</c>（32–4096 / 16–4096）、VM 的字段初值与读取兜底（200 / 256，写了两遍）、
    /// 设置页滑杆的 <c>Minimum/Maximum</c>（<b>32–2048 / 16–1024</b>），而模式预设（384/512/160/128）在第五处。
    /// <para><b>为什么这不是"只是重复"而是缺陷</b>：滑杆的上限比仓储小一半，于是存档里一个<b>合法且真在生效</b>的
    /// 预算（例如 3000 MB，<c>MemoryReclaimer</c> 就是按它回收的）在设置页<b>表达不出来</b>——
    /// 滑杆最多只能指到 2048。界面与真值各拿一份范围，迟早这样分岔（#175）。
    /// <b>注：这里只声称能证的那一半。</b>被截断的显示会不会再写回存档，取决于 <c>RangeBase.Value</c> 的取值强制——
    /// 官方对该属性的措辞只到 "which may be coerced"，没写"绑定会把截断值写回来源"，
    /// 所以那句话<b>不当结论用</b>（批次 SA 自查时把它从注释里删了；收口之后两边同量程，这问题按构造不存在）。</para>
    /// </summary>
    public const double BudgetMbFloor = 32.0;
    /// <summary>进程内存预算的上限（MB）。以前界面写 2048、仓储写 4096，两处不是同一个数。</summary>
    public const double BudgetMbCeiling = 4096.0;
    /// <summary>没存过时的进程内存预算（MB）。<b>读取兜底与界面初值都从这里取</b>，不再各写一个 200。</summary>
    public const double BudgetMbDefault = 200.0;
    /// <summary>滑杆步进（MB）——也是"用户能选到的粒度"，跟着范围一起放这里，免得界面再自己定一个。</summary>
    public const double BudgetMbStep = 16.0;

    /// <summary>有界缓存条目数的下限。</summary>
    public const int CacheCountFloor = 16;
    /// <summary>有界缓存条目数的上限（以前界面写 1024、仓储写 4096）。</summary>
    public const int CacheCountCeiling = 4096;
    /// <summary>没存过时的有界缓存上限（条）。</summary>
    public const int CacheCountDefault = 256;
    /// <summary>滑杆步进（条）。</summary>
    public const int CacheCountStep = 16;

    /// <summary>把预算夹进合法范围（读档与落盘<b>同一颗</b>，两边各写一次 <c>Math.Clamp</c> 就是两份真值）。</summary>
    public static double NormalizeBudgetMb(double mb) => Math.Clamp(mb, BudgetMbFloor, BudgetMbCeiling);

    /// <summary>把缓存条目上限夹进合法范围。</summary>
    public static int NormalizeCacheCount(int count) => Math.Clamp(count, CacheCountFloor, CacheCountCeiling);

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
