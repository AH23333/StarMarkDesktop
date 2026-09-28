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
/// SettingsStore 的这一段——外观与材质那组设置：主题、磁吸、背衬、透明度、材质强度（组件与主窗各自一套）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    public ThemePreference LoadTheme() => Load() is { } d && Enum.IsDefined(typeof(ThemePreference), d.Theme)
        ? (ThemePreference)d.Theme
        : ThemePreference.Default;

    public void SaveTheme(ThemePreference pref)
    {
        var d = Load() ?? new SettingsData();
        d.Theme = (int)pref;
        Save(d);
    }

    /// <summary>组件边缘磁吸总开关（默认开启）。关闭后拖动/缩放都不再自动贴合。</summary>
    public bool LoadWidgetSnapEnabled() => Load() is { } d ? d.WidgetSnapEnabled ?? true : true;

    public void SaveWidgetSnapEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapEnabled = enabled;
        Save(d);
    }

    /// <summary>磁吸对齐间距（逻辑像素，默认 8，范围 0–40）：两组件贴合时保留的间隙。</summary>
    public int LoadWidgetSnapSpacing()
        => Load() is { } d && d.WidgetSnapSpacing is { } v
            ? Math.Clamp(v, 0, 40)
            : StarMark.Core.Widgets.WidgetSnapCalculator.DefaultSpacing;

    public void SaveWidgetSnapSpacing(int px)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapSpacing = Math.Clamp(px, 0, 40);
        Save(d);
    }

    /// <summary>磁吸吸附强度＝进入吸附阈值（逻辑像素，默认 24，范围 4–64）：越大越早吸附。</summary>
    public int LoadWidgetSnapStrength()
        => Load() is { } d && d.WidgetSnapStrength is { } v
            ? Math.Clamp(v, 4, 64)
            : StarMark.Core.Widgets.WidgetSnapCalculator.DefaultEngageThreshold;

    public void SaveWidgetSnapStrength(int px)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapStrength = Math.Clamp(px, 4, 64);
        Save(d);
    }

    /// <summary>
    /// 组件窗口背景材质（默认亚克力）。
    /// 注意：旧版 settings.json 没有该字段（WidgetBackdrop 为 null），
    /// 此处必须先取值的空合并结果再校验枚举，绝不能直接对可空值取 .Value
    /// （历史 bug：先 `Enum.IsDefined(typeof(...), d.WidgetBackdrop ?? 0)` 通过后再 `d.WidgetBackdrop!.Value`
    /// 会对 null 解包，抛 "Nullable object must have a value"，导致设置页导航与组件显示直接崩溃）。
    /// </summary>
    public WidgetBackdropKind LoadWidgetBackdrop()
    {
        if (Load() is { } d && d.WidgetBackdrop is { } raw
            && Enum.IsDefined(typeof(WidgetBackdropKind), raw))
            return (WidgetBackdropKind)raw;
        return WidgetBackdropKind.Acrylic;
    }

    public void SaveWidgetBackdrop(WidgetBackdropKind kind)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetBackdrop = (int)kind;
        Save(d);
    }

    /// <summary>组件背景不透明度（默认 0.72）。越接近 1 越不透明。</summary>
    public double LoadWidgetOpacity()
    {
        if (Load() is { } d && d.WidgetOpacity is > 0)
            return Math.Clamp(d.WidgetOpacity.Value, 0.3, 1.0);
        return WidgetAppearance.DefaultOpacity;
    }

    public void SaveWidgetOpacity(double opacity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetOpacity = Math.Clamp(opacity, 0.3, 1.0);
        Save(d);
    }

    /// <summary>主窗口背景不透明度（独立于组件）。从未单设则回落到组件不透明度（升级观感不变）。</summary>
    public double LoadMainWindowOpacity()
    {
        if (Load() is { } d && d.MainWindowOpacity is > 0)
            return Math.Clamp(d.MainWindowOpacity.Value, 0.3, 1.0);
        return LoadWidgetOpacity();
    }

    public void SaveMainWindowOpacity(double opacity)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowOpacity = Math.Clamp(opacity, 0.3, 1.0);
        Save(d);
    }

    /// <summary>毛玻璃材质浓度（默认 0.65）：控制亚克力的染色/光亮度，值越大越「实」。</summary>
    public double LoadWidgetMaterialIntensity()
    {
        if (Load() is { } d && d.WidgetMaterialIntensity is >= 0)
            return Math.Clamp(d.WidgetMaterialIntensity.Value, 0.0, 1.0);
        return 0.65;
    }

    public void SaveWidgetMaterialIntensity(double intensity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetMaterialIntensity = Math.Clamp(intensity, 0.0, 1.0);
        Save(d);
    }

    /// <summary>主窗口是否跟随同样的半透明材质（默认开启）。</summary>
    public bool LoadMainWindowTranslucent() => Load() is { } d ? d.MainWindowTranslucent ?? true : true;

    public void SaveMainWindowTranslucent(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowTranslucent = enabled;
        Save(d);
    }

    /// <summary>
    /// 主窗口背景材质（独立于组件）。新配置直接读 <see cref="SettingsData.MainWindowBackdrop"/>；
    /// 旧配置（从未单设过主窗材质、只有「主窗口使用同一材质」布尔）按原语义迁移：
    /// 开关开 → 沿用组件材质，关 → 实色不透明（None）。这样升级后主窗口观感与升级前完全一致。
    /// </summary>
    public WidgetBackdropKind LoadMainWindowBackdrop()
    {
        if (Load() is { } d && d.MainWindowBackdrop is { } raw
            && Enum.IsDefined(typeof(WidgetBackdropKind), raw))
            return (WidgetBackdropKind)raw;
        return LoadMainWindowTranslucent() ? LoadWidgetBackdrop() : WidgetBackdropKind.None;
    }

    public void SaveMainWindowBackdrop(WidgetBackdropKind kind)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowBackdrop = (int)kind;
        // 同步写旧布尔，保证仍以 MainWindowTranslucent 读取的历史路径（若有）语义不漂移。
        d.MainWindowTranslucent = kind != WidgetBackdropKind.None;
        Save(d);
    }
}
