#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——性能模式那组：模式、缓存预算、图片缓存上限。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>性能模式（默认均衡）。旧版 settings.json 无该字段时回退均衡。</summary>
    public PerformanceMode LoadPerformanceMode()
    {
        if (Load() is { } d && d.PerformanceMode is { } raw && Enum.IsDefined(typeof(PerformanceMode), raw))
            return (PerformanceMode)raw;
        return PerformanceMode.Balanced;
    }

    public void SavePerformanceMode(PerformanceMode mode)
    {
        var d = Load() ?? new SettingsData();
        d.PerformanceMode = (int)mode;
        Save(d);
    }

    /// <summary>
    /// 开机要不要把组件窗全建出来。<b>只有存档明确写着 false 才关</b>——旧档没这个字段、
    /// 或值被别的程序写坏，都按"照今天的样子开"处理：宁可多占内存，也不要在用户不知情时收起他的桌面。
    /// </summary>
    public bool LoadWidgetsLoadOnStartup()
        => Load()?.WidgetsLoadOnStartup is not false;

    public void SaveWidgetsLoadOnStartup(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetsLoadOnStartup = enabled;
        Save(d);
    }

    /// <summary>自定义模式下的进程工作集预算（MB）。范围与默认都取自 <see cref="PerformanceSettingsPolicy"/>——这里不再自己写边界（批次 SA，P-123 第 3 条）。</summary>
    public double LoadCacheBudgetMb()
    {
        // `is > 0` 那一道不能省：存档里的 0 / 负数意思是"没存过"，要落默认值，而不是被夹成下限
        if (Load() is { } d && d.CacheBudgetMb is > 0)
            return PerformanceSettingsPolicy.NormalizeBudgetMb(d.CacheBudgetMb.Value);
        return PerformanceSettingsPolicy.BudgetMbDefault;
    }

    public void SaveCacheBudgetMb(double mb)
    {
        var d = Load() ?? new SettingsData();
        d.CacheBudgetMb = PerformanceSettingsPolicy.NormalizeBudgetMb(mb);
        Save(d);
    }

    /// <summary>自定义模式下的有界缓存最大条目数。范围与默认同取自 <see cref="PerformanceSettingsPolicy"/>。</summary>
    public int LoadMaxImageCacheCount()
    {
        if (Load() is { } d && d.MaxImageCacheCount is > 0)
            return PerformanceSettingsPolicy.NormalizeCacheCount(d.MaxImageCacheCount.Value);
        return PerformanceSettingsPolicy.CacheCountDefault;
    }

    public void SaveMaxImageCacheCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxImageCacheCount = PerformanceSettingsPolicy.NormalizeCacheCount(count);
        Save(d);
    }
}
